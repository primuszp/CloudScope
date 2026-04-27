using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// 3D Oriented Bounding Box selection tool with handle-based editing.
    ///
    /// Phase 1 (Placement): Draw 2D rectangle on screen → auto-creates 3D box
    ///   aligned to camera view with depth from depth-buffer sampling.
    /// Phase 2 (Editing): 15 interactive handles (6 face + 8 corner + 1 center)
    ///   for direct manipulation, plus G/S/R keyboard shortcuts.
    /// Enter to confirm, Escape to cancel.
    /// </summary>
    public sealed class BoxSelectionTool : ISelectionTool
    {
        public SelectionToolType ToolType => SelectionToolType.Box;
        public bool IsActive { get; private set; }
        public bool IsEditing { get; private set; }
        public bool HasVolume => (IsActive || IsEditing) &&
                                 HalfExtents.X > 1e-4f && HalfExtents.Y > 1e-4f && HalfExtents.Z > 1e-4f;

        // ── OBB parameters (world-space) ──────────────────────────────────────
        public Vector3 Center;
        public Vector3 HalfExtents;
        public Quaternion Rotation = Quaternion.Identity;

        // ── Placement state (screen-space 2D rectangle) ───────────────────────
        public int StartX, StartY, EndX, EndY;  // screen pixels
        private bool _placementDrag;

        // ── Handle system ─────────────────────────────────────────────────────
        public const int HandleCount = 15;
        public const int HoverNone = -1;
        public int HoveredHandle = HoverNone;

        /// <summary>Handle local positions in OBB space (range -1 to +1).</summary>
        public static readonly Vector3[] HandleLocalPos =
        {
            // 0-5: Face centers
            new(-1, 0, 0), // 0 FaceLeft   (-X)
            new( 1, 0, 0), // 1 FaceRight  (+X)
            new( 0,-1, 0), // 2 FaceBottom (-Y)
            new( 0, 1, 0), // 3 FaceTop    (+Y)
            new( 0, 0,-1), // 4 FaceNear   (-Z)
            new( 0, 0, 1), // 5 FaceFar    (+Z)
            // 6-13: Corners
            new(-1,-1,-1), new( 1,-1,-1), new(-1, 1,-1), new( 1, 1,-1),
            new(-1,-1, 1), new( 1,-1, 1), new(-1, 1, 1), new( 1, 1, 1),
            // 14: Center
            new( 0, 0, 0),
        };

        public static bool IsFaceHandle(int i) => i >= 0 && i <= 5;
        public static bool IsCornerHandle(int i) => i >= 6 && i <= 13;
        public static bool IsCenterHandle(int i) => i == 14;

        // ── Edit state ────────────────────────────────────────────────────────
        private EditAction _action = EditAction.None;
        private int _axis = -1;
        private int _activeHandle = HoverNone;
        private int _editStartX, _editStartY;
        private float _editViewZ;  // cached view-space depth for smooth dragging
        private Vector3 _editStartCenter;
        private Vector3 _editStartExtents;
        private Quaternion _editStartRotation;

        // ── Phase 1: Placement (2D screen rectangle) ──────────────────────────

        public void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            StartX = EndX = mx;
            StartY = EndY = my;
            _placementDrag = true;
            IsActive = true;
            IsEditing = false;
            HalfExtents = Vector3.Zero;
        }

        public void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!_placementDrag) return;
            EndX = mx;
            EndY = my;
        }

        public void OnMouseUp(int mx, int my)
        {
            if (!_placementDrag) return;
            EndX = mx;
            EndY = my;
            _placementDrag = false;
            IsActive = false;

            // Need minimum size (at least 5 pixels)
            if (Math.Abs(EndX - StartX) < 5 && Math.Abs(EndY - StartY) < 5)
                return;

            // Box creation will be finalized by FinalizeBoxFromScreen()
            // called from PointCloudViewer after mouse-up
            IsEditing = true;
        }

        /// <summary>
        /// Creates the 3D box from the screen rectangle, aligned to the camera view.
        /// Must be called after OnMouseUp while the GL context is active.
        /// </summary>
        public void FinalizeBoxFromScreen(OrbitCamera cam)
        {
            int x0 = Math.Min(StartX, EndX), x1 = Math.Max(StartX, EndX);
            int y0 = Math.Min(StartY, EndY), y1 = Math.Max(StartY, EndY);
            int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;

            // Sample depth at center of rectangle
            var (centerWorld, centerHit) = cam.ScreenToWorldPoint(cx, cy, 11);
            float refViewZ = cam.WorldToViewZ(centerWorld);

            // Sample depth at edges and corners for depth range
            float minViewZ = refViewZ, maxViewZ = refViewZ;
            int[] sampleXs = { x0, cx, x1, x0, x1, x0, cx, x1 };
            int[] sampleYs = { y0, y0, y0, cy, cy, y1, y1, y1 };
            for (int i = 0; i < sampleXs.Length; i++)
            {
                var (pt, hit) = cam.ScreenToWorldPoint(sampleXs[i], sampleYs[i], 5);
                if (hit)
                {
                    float vz = cam.WorldToViewZ(pt);
                    if (vz < minViewZ) minViewZ = vz;
                    if (vz > maxViewZ) maxViewZ = vz;
                }
            }

            // Get world-space corners at reference depth
            Vector3 topLeft  = cam.ScreenToWorldAtDepth(x0, y0, refViewZ);
            Vector3 topRight = cam.ScreenToWorldAtDepth(x1, y0, refViewZ);
            Vector3 botLeft  = cam.ScreenToWorldAtDepth(x0, y1, refViewZ);

            // Box orientation from camera view axes
            Vector3 axisX = (topRight - topLeft);
            Vector3 axisY = (topLeft - botLeft);
            float halfW = axisX.Length * 0.5f;
            float halfH = axisY.Length * 0.5f;

            if (halfW < 0.001f || halfH < 0.001f)
            {
                IsEditing = false;
                return;
            }

            axisX.Normalize();
            axisY.Normalize();
            Vector3 axisZ = Vector3.Cross(axisX, axisY).Normalized();
            // Re-orthogonalize Y to ensure a proper orthonormal basis
            axisY = Vector3.Cross(axisZ, axisX).Normalized();

            // Build rotation: in row-major convention (v * M), rows are the transformed basis
            // Row0 = where local X maps to in world = axisX
            // Row1 = where local Y maps to in world = axisY
            // Row2 = where local Z maps to in world = axisZ
            Matrix3 rotMat = new Matrix3(axisX, axisY, axisZ);
            Rotation = Quaternion.FromMatrix(rotMat);
            Rotation.Normalize();

            // Verify round-trip: quaternion→matrix should reproduce the same rotation
            Matrix3 check = Matrix3.CreateFromQuaternion(Rotation);
            Console.WriteLine($"Box created: extents=({halfW:F2},{halfH:F2}), rot check dot={Vector3.Dot(check.Row0, axisX):F4}");

            // Depth extent from depth samples
            float depthRange = Math.Abs(maxViewZ - minViewZ);
            float halfD = Math.Max(depthRange * 0.5f, Math.Max(halfW, halfH) * 0.3f);

            // Center at the midpoint of the depth range
            float midViewZ = (minViewZ + maxViewZ) * 0.5f;
            Center = cam.ScreenToWorldAtDepth(cx, cy, midViewZ);

            HalfExtents = new Vector3(halfW, halfH, halfD);
        }

        // ── Phase 2: Handle interaction ───────────────────────────────────────

        /// <summary>
        /// Test which handle is under the mouse cursor. Returns handle index or HoverNone.
        /// </summary>
        public int HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f)
        {
            int best = HoverNone;
            float bestDist = threshold;

            for (int i = 0; i < HandleCount; i++)
            {
                Vector3 worldPos = HandleWorldPosition(i);
                var (sx, sy, behind) = cam.WorldToScreen(worldPos);
                if (behind) continue;

                float dist = MathF.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>World-space position of handle i, computed via the model matrix
        /// to guarantee exact alignment with the wireframe.</summary>
        public Vector3 HandleWorldPosition(int i)
        {
            Matrix4 model = GetModelMatrix();
            Vector4 wp = new Vector4(HandleLocalPos[i], 1f) * model;
            return wp.Xyz;
        }

        /// <summary>Start dragging a specific handle.</summary>
        public void BeginHandleDrag(int handleIdx, int mx, int my, OrbitCamera cam)
        {
            _activeHandle = handleIdx;
            _editStartX = mx; _editStartY = my;
            _editStartCenter = Center;
            _editStartExtents = HalfExtents;
            _editViewZ = cam.WorldToViewZ(HandleWorldPosition(handleIdx));
        }

        /// <summary>Update handle drag.</summary>
        public void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle == HoverNone) return;

            // Get world delta from mouse movement at consistent depth
            Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
            Vector3 curWorld = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
            Vector3 worldDelta = curWorld - startWorld;

            if (IsCenterHandle(_activeHandle))
            {
                // Move the entire box
                Center = _editStartCenter + worldDelta;
            }
            else
            {
                // OpenTK Matrix3 * Vector3 does column-vector multiplication:
                //   rotMat * v  → projects v onto each row (world→local)
                //   invRotMat * v (= rotMat^T * v) → expresses local v in world coords (local→world)
                Matrix3 rotMat    = Matrix3.CreateFromQuaternion(Rotation);
                Matrix3 invRotMat = Matrix3.Transpose(rotMat);

                // World delta → local delta: project world movement onto box axes
                Vector3 localDelta = rotMat * worldDelta;

                if (IsFaceHandle(_activeHandle))
                {
                    // Single-axis resize: move one face, opposite face stays fixed
                    int faceAxis = _activeHandle / 2;  // 0,1→X  2,3→Y  4,5→Z
                    float sign = (_activeHandle % 2 == 0) ? -1f : 1f; // even=negative, odd=positive face

                    float axisDelta = faceAxis switch
                    {
                        0 => localDelta.X * sign,
                        1 => localDelta.Y * sign,
                        _ => localDelta.Z * sign,
                    };

                    float newExtent = Math.Max(_editStartExtents[faceAxis] + axisDelta * 0.5f, 0.01f);
                    float extentChange = newExtent - _editStartExtents[faceAxis];

                    // Update extent
                    HalfExtents = faceAxis switch
                    {
                        0 => new Vector3(newExtent, _editStartExtents.Y, _editStartExtents.Z),
                        1 => new Vector3(_editStartExtents.X, newExtent, _editStartExtents.Z),
                        _ => new Vector3(_editStartExtents.X, _editStartExtents.Y, newExtent),
                    };

                    // Shift center so opposite face stays in place (local→world)
                    Vector3 localAxisDir = faceAxis switch
                    {
                        0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ
                    };
                    Vector3 worldAxisDir = invRotMat * localAxisDir;
                    Center = _editStartCenter + worldAxisDir * (sign * extentChange);
                }
                else // Corner handle
                {
                    // Corner resize: 3-axis resize, opposite corner stays fixed
                    Vector3 cornerSign = HandleLocalPos[_activeHandle]; // (±1, ±1, ±1)
                    float dex = localDelta.X * cornerSign.X;
                    float dey = localDelta.Y * cornerSign.Y;
                    float dez = localDelta.Z * cornerSign.Z;

                    Vector3 newExtents = new Vector3(
                        Math.Max(_editStartExtents.X + dex * 0.5f, 0.01f),
                        Math.Max(_editStartExtents.Y + dey * 0.5f, 0.01f),
                        Math.Max(_editStartExtents.Z + dez * 0.5f, 0.01f));

                    Vector3 extentChange = newExtents - _editStartExtents;
                    HalfExtents = newExtents;

                    // Shift center so opposite corner stays fixed (local→world)
                    Vector3 localShift = new Vector3(
                        cornerSign.X * extentChange.X,
                        cornerSign.Y * extentChange.Y,
                        cornerSign.Z * extentChange.Z);
                    Center = _editStartCenter + invRotMat * localShift;
                }
            }
        }

        /// <summary>End handle drag.</summary>
        public void EndHandleDrag()
        {
            _activeHandle = HoverNone;
        }

        public bool IsHandleDragging => _activeHandle != HoverNone;

        // ── ISelectionTool: G/S/R keyboard editing (kept for convenience) ─────

        public void BeginGrab(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _action = EditAction.Grab;
            _editStartX = mx; _editStartY = my;
            _editStartCenter = Center;
            _editViewZ = camera.WorldToViewZ(Center);
        }

        public void BeginScale(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _action = EditAction.Scale;
            _editStartX = mx; _editStartY = my;
            _editStartExtents = HalfExtents;
        }

        public void BeginRotate(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _action = EditAction.Rotate;
            _editStartX = mx; _editStartY = my;
            _editStartRotation = Rotation;
        }

        public void UpdateEdit(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            int dx = mx - _editStartX;

            switch (_action)
            {
                case EditAction.Grab:
                {
                    Vector3 startW = camera.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 curW = camera.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    Vector3 delta = curW - startW;
                    if (_axis >= 0)
                    {
                        Vector3 mask = _axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                        delta = mask * Vector3.Dot(delta, mask);
                    }
                    Center = _editStartCenter + delta;
                    break;
                }
                case EditAction.Scale:
                {
                    float factor = MathF.Max(1f + dx * 0.005f, 0.05f);
                    if (_axis < 0)
                        HalfExtents = _editStartExtents * factor;
                    else
                    {
                        HalfExtents = _editStartExtents;
                        float val = MathF.Max(_editStartExtents[_axis] * factor, 0.01f);
                        HalfExtents = _axis switch
                        {
                            0 => new Vector3(val, HalfExtents.Y, HalfExtents.Z),
                            1 => new Vector3(HalfExtents.X, val, HalfExtents.Z),
                            _ => new Vector3(HalfExtents.X, HalfExtents.Y, val),
                        };
                    }
                    break;
                }
                case EditAction.Rotate:
                {
                    float angle = dx * 0.5f * MathF.PI / 180f;
                    Vector3 rotAxis = _axis switch
                    {
                        0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ,
                    };
                    Rotation = _editStartRotation * Quaternion.FromAxisAngle(rotAxis, angle);
                    Rotation.Normalize();
                    break;
                }
            }
        }

        public void EndEdit()
        {
            _action = EditAction.None;
            _axis = -1;
        }

        public void AdjustScale(float delta)
        {
            if (!IsEditing) return;
            float factor = MathF.Max(1f + delta * 0.08f, 0.05f);
            if (_axis < 0) HalfExtents *= factor;
            else
            {
                float val = MathF.Max(HalfExtents[_axis] * factor, 0.01f);
                HalfExtents = _axis switch
                {
                    0 => new Vector3(val, HalfExtents.Y, HalfExtents.Z),
                    1 => new Vector3(HalfExtents.X, val, HalfExtents.Z),
                    _ => new Vector3(HalfExtents.X, HalfExtents.Y, val),
                };
            }
        }

        public void SetAxisConstraint(int axis) => _axis = axis;

        public void Confirm() { IsEditing = false; IsActive = false; }

        public void Cancel()
        {
            IsActive = false; IsEditing = false; _placementDrag = false;
            _action = EditAction.None; _axis = -1; _activeHandle = HoverNone;
            HalfExtents = Vector3.Zero;
        }

        // ── Selection resolution ──────────────────────────────────────────────

        public HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            var result = new HashSet<int>();
            if (HalfExtents.X < 1e-4f || HalfExtents.Y < 1e-4f || HalfExtents.Z < 1e-4f)
                return result;

            // rotMat * v = world→local (rows are box axes; dot-products project v onto each axis)
            Matrix3 rotMat = Matrix3.CreateFromQuaternion(Rotation);
            float hx = HalfExtents.X, hy = HalfExtents.Y, hz = HalfExtents.Z;
            float cx = Center.X, cy = Center.Y, cz = Center.Z;

            for (int i = 0; i < points.Length; i++)
            {
                Vector3 local = rotMat * new Vector3(points[i].X - cx, points[i].Y - cy, points[i].Z - cz);
                if (MathF.Abs(local.X) <= hx && MathF.Abs(local.Y) <= hy && MathF.Abs(local.Z) <= hz)
                    result.Add(i);
            }
            return result;
        }

        // ── Helpers for renderer ──────────────────────────────────────────────

        public EditAction CurrentAction => _activeHandle != HoverNone ? EditAction.Grab : _action;
        public int AxisConstraint => _axis;

        public Matrix4 GetModelMatrix()
        {
            return Matrix4.CreateScale(HalfExtents)
                 * Matrix4.CreateFromQuaternion(Rotation)
                 * Matrix4.CreateTranslation(Center);
        }
    }
}
