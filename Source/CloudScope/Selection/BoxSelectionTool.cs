using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public enum ToolPhase { Idle, Drawing, Extruding, Editing }

    /// <summary>
    /// 3-phase OBB selection tool.
    ///   Phase 1 – Drawing:   drag to create 2D screen rectangle
    ///   Phase 2 – Extruding: move mouse to set depth (click to confirm)
    ///   Phase 3 – Editing:   6 face handles, 8 corner handles, 1 center handle,
    ///                         3 rotation rings (local X/Y/Z)
    /// </summary>
    public sealed class BoxSelectionTool : ISelectionTool
    {
        public SelectionToolType ToolType => SelectionToolType.Box;

        // ISelectionTool compat
        public bool IsActive  => Phase == ToolPhase.Drawing;
        public bool IsEditing => Phase == ToolPhase.Extruding || Phase == ToolPhase.Editing;
        public bool HasVolume => Phase != ToolPhase.Idle && HalfExtents.LengthSquared > 1e-8f;

        public ToolPhase Phase { get; private set; } = ToolPhase.Idle;

        // ── OBB ──────────────────────────────────────────────────────────────
        public Vector3    Center;
        public Vector3    HalfExtents;
        public Quaternion Rotation = Quaternion.Identity;

        // ── Phase 1: Drawing ─────────────────────────────────────────────────
        public int StartX, StartY, EndX, EndY;

        // ── Phase 2: Extruding ───────────────────────────────────────────────
        private Vector3 _screenFaceCenter;   // world pos of the front face center (fixed)
        private float   _worldPerPixel;      // lateral world units per screen pixel at front-face depth
        private int     _extrudeRefY;        // screen Y at which extrude started

        // ── Handle system ────────────────────────────────────────────────────
        // 0-5:  face centers (-X,+X,-Y,+Y,-Z,+Z)
        // 6-13: corners
        // 14:   center
        // 15-17: rotation rings (local X, Y, Z)
        public const int HandleCount = 18;
        public const int HoverNone   = -1;
        public int HoveredHandle = HoverNone;

        public static readonly Vector3[] HandleLocalPos = new Vector3[15];
        static BoxSelectionTool()
        {
            HandleLocalPos[0]  = new(-1, 0, 0);  HandleLocalPos[1]  = new( 1, 0, 0);
            HandleLocalPos[2]  = new( 0,-1, 0);  HandleLocalPos[3]  = new( 0, 1, 0);
            HandleLocalPos[4]  = new( 0, 0,-1);  HandleLocalPos[5]  = new( 0, 0, 1);
            HandleLocalPos[6]  = new(-1,-1,-1);   HandleLocalPos[7]  = new( 1,-1,-1);
            HandleLocalPos[8]  = new(-1, 1,-1);   HandleLocalPos[9]  = new( 1, 1,-1);
            HandleLocalPos[10] = new(-1,-1, 1);   HandleLocalPos[11] = new( 1,-1, 1);
            HandleLocalPos[12] = new(-1, 1, 1);   HandleLocalPos[13] = new( 1, 1, 1);
            HandleLocalPos[14] = Vector3.Zero;
        }

        public static bool IsFaceHandle(int i)  => i >= 0  && i <= 5;
        public static bool IsCornerHandle(int i) => i >= 6  && i <= 13;
        public static bool IsCenterHandle(int i) => i == 14;
        public static bool IsRingHandle(int i)   => i >= 15 && i <= 17;
        public static int  RingAxis(int i)        => i - 15; // 0=X, 1=Y, 2=Z

        // ── Edit state ───────────────────────────────────────────────────────
        private int        _activeHandle     = HoverNone;
        private int        _editStartX, _editStartY;
        private float      _editViewZ;
        private Vector3    _editStartCenter;
        private Vector3    _editStartExtents;
        private Quaternion _editStartRotation;
        private float      _ringStartAngle;  // screen-angle at ring drag start

        // G/S/R keyboard edit state
        private EditAction _kbAction = EditAction.None;
        private int        _kbAxis   = -1;

        // ────────────────────────────────────────────────────────────────────
        // Phase 1: Drawing
        // ────────────────────────────────────────────────────────────────────

        public void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            StartX = EndX = mx;
            StartY = EndY = my;
            Phase = ToolPhase.Drawing;
            HalfExtents = Vector3.Zero;
            Rotation = Quaternion.Identity;
        }

        public void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (Phase == ToolPhase.Drawing) { EndX = mx; EndY = my; }
        }

        public void OnMouseUp(int mx, int my)
        {
            if (Phase != ToolPhase.Drawing) return;
            EndX = mx; EndY = my;
            if (Math.Abs(EndX - StartX) < 5 && Math.Abs(EndY - StartY) < 5)
                Phase = ToolPhase.Idle;
            // FinalizeBoxFromScreen called by viewer after this
        }

        public void FinalizeBoxFromScreen(OrbitCamera cam)
        {
            int x0 = Math.Min(StartX, EndX), x1 = Math.Max(StartX, EndX);
            int y0 = Math.Min(StartY, EndY), y1 = Math.Max(StartY, EndY);
            int cxs = (x0 + x1) / 2, cys = (y0 + y1) / 2;

            var (centerWorld, _) = cam.ScreenToWorldPoint(cxs, cys, 11);
            float refViewZ = cam.WorldToViewZ(centerWorld);

            Vector3 topLeft  = cam.ScreenToWorldAtDepth(x0, y0, refViewZ);
            Vector3 topRight = cam.ScreenToWorldAtDepth(x1, y0, refViewZ);
            Vector3 botLeft  = cam.ScreenToWorldAtDepth(x0, y1, refViewZ);

            Vector3 axisX = topRight - topLeft;
            Vector3 axisY = topLeft  - botLeft;
            float halfW = axisX.Length * 0.5f;
            float halfH = axisY.Length * 0.5f;

            if (halfW < 0.001f || halfH < 0.001f) { Phase = ToolPhase.Idle; return; }

            axisX.Normalize();
            axisY.Normalize();
            Vector3 axisZ = Vector3.Cross(axisX, axisY).Normalized();
            axisY = Vector3.Cross(axisZ, axisX).Normalized();

            // Build rotation: in OpenTK row-major convention, rows map local→world axes
            Matrix3 rotMat = new Matrix3(axisX, axisY, axisZ);
            Rotation = Quaternion.FromMatrix(rotMat);
            Rotation.Normalize();

            _screenFaceCenter = cam.ScreenToWorldAtDepth(cxs, cys, refViewZ);
            _worldPerPixel    = cam.ScreenToWorldRadius(_screenFaceCenter, cxs + 1, cys);
            _extrudeRefY      = cys;

            // Start with a visible depth equal to the larger screen dimension
            float initialHalfD = Math.Max(halfW, halfH);

            Matrix3 initInvRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));
            Vector3 initWorldZ = initInvRot * Vector3.UnitZ;
            Center      = _screenFaceCenter + initWorldZ * initialHalfD;
            HalfExtents = new Vector3(halfW, halfH, initialHalfD);
            Phase       = ToolPhase.Extruding;
        }

        // ────────────────────────────────────────────────────────────────────
        // Phase 2: Extruding
        // ────────────────────────────────────────────────────────────────────

        public void UpdateExtrude(int mx, int my)
        {
            if (Phase != ToolPhase.Extruding) return;
            int dy = _extrudeRefY - my;         // move up = deeper
            float halfD = Math.Max(Math.Abs(dy) * _worldPerPixel, 0.001f);

            // Extend along local Z (the camera view-Z axis of the drawn rect).
            // Front face stays at _screenFaceCenter, back face moves.
            // Center = frontFace + localZ * halfD
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));
            Vector3 worldZ  = invRot * Vector3.UnitZ;   // local +Z in world
            Center      = _screenFaceCenter + worldZ * halfD;
            HalfExtents = new Vector3(HalfExtents.X, HalfExtents.Y, halfD);
        }

        public void ConfirmExtrude()
        {
            if (Phase == ToolPhase.Extruding) Phase = ToolPhase.Editing;
        }

        // ────────────────────────────────────────────────────────────────────
        // Phase 3: Handle interaction
        // ────────────────────────────────────────────────────────────────────

        public int HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f)
        {
            if (Phase != ToolPhase.Editing) return HoverNone;

            int   best     = HoverNone;
            float bestDist = threshold;

            // Face / corner / center handles
            for (int i = 0; i < 15; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(HandleWorldPosition(i));
                if (behind) continue;
                float d = MathF.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (d < bestDist) { bestDist = d; best = i; }
            }

            // Rotation ring handles
            for (int r = 0; r < 3; r++)
            {
                float d = RingScreenDistance(r, mx, my, cam);
                if (d < bestDist) { bestDist = d; best = 15 + r; }
            }

            return best;
        }

        public Vector3 HandleWorldPosition(int i)
        {
            if (i >= 15) return Center;
            Matrix4 model = GetModelMatrix();
            Vector4 wp = new Vector4(HandleLocalPos[i], 1f) * model;
            return wp.Xyz;
        }

        public void BeginHandleDrag(int handleIdx, int mx, int my, OrbitCamera cam)
        {
            _activeHandle      = handleIdx;
            _editStartX        = mx;
            _editStartY        = my;
            _editStartCenter   = Center;
            _editStartExtents  = HalfExtents;
            _editStartRotation = Rotation;

            if (IsRingHandle(handleIdx))
            {
                var (cx, cy, _) = cam.WorldToScreen(Center);
                _ringStartAngle = MathF.Atan2(my - cy, mx - cx);
            }
            else
            {
                _editViewZ = cam.WorldToViewZ(HandleWorldPosition(handleIdx));
            }
        }

        public void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle == HoverNone) return;

            if (IsRingHandle(_activeHandle))
            {
                UpdateRingDrag(mx, my, cam);
                return;
            }

            Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
            Vector3 curWorld   = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
            Vector3 worldDelta = curWorld - startWorld;

            if (IsCenterHandle(_activeHandle))
            {
                Center = _editStartCenter + worldDelta;
                return;
            }

            // rotMat * v = world→local;  invRotMat * v = local→world
            Matrix3 rotMat    = Matrix3.CreateFromQuaternion(Rotation);
            Matrix3 invRotMat = Matrix3.Transpose(rotMat);
            Vector3 localDelta = rotMat * worldDelta;

            if (IsFaceHandle(_activeHandle))
            {
                int   faceAxis = _activeHandle / 2;
                float sign     = (_activeHandle % 2 == 0) ? -1f : 1f;

                float axisDelta = faceAxis switch
                {
                    0 => localDelta.X * sign,
                    1 => localDelta.Y * sign,
                    _ => localDelta.Z * sign,
                };

                float newExtent    = Math.Max(_editStartExtents[faceAxis] + axisDelta * 0.5f, 0.01f);
                float extentChange = newExtent - _editStartExtents[faceAxis];

                HalfExtents = faceAxis switch
                {
                    0 => new(newExtent, _editStartExtents.Y, _editStartExtents.Z),
                    1 => new(_editStartExtents.X, newExtent, _editStartExtents.Z),
                    _ => new(_editStartExtents.X, _editStartExtents.Y, newExtent),
                };

                Vector3 localAxis = faceAxis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                Center = _editStartCenter + invRotMat * localAxis * (sign * extentChange);
            }
            else // corner
            {
                Vector3 cs  = HandleLocalPos[_activeHandle]; // (±1,±1,±1)
                Vector3 de  = new(localDelta.X * cs.X, localDelta.Y * cs.Y, localDelta.Z * cs.Z);

                Vector3 newExt = new(
                    Math.Max(_editStartExtents.X + de.X * 0.5f, 0.01f),
                    Math.Max(_editStartExtents.Y + de.Y * 0.5f, 0.01f),
                    Math.Max(_editStartExtents.Z + de.Z * 0.5f, 0.01f));

                Vector3 shift = newExt - _editStartExtents;
                HalfExtents   = newExt;
                Center        = _editStartCenter + invRotMat * new Vector3(cs.X * shift.X, cs.Y * shift.Y, cs.Z * shift.Z);
            }
        }

        private void UpdateRingDrag(int mx, int my, OrbitCamera cam)
        {
            var (cx, cy, _) = cam.WorldToScreen(Center);
            float currentAngle = MathF.Atan2(my - cy, mx - cx);
            float delta = currentAngle - _ringStartAngle;

            // Normalize to [-π, π]
            while (delta >  MathF.PI) delta -= MathF.Tau;
            while (delta < -MathF.PI) delta += MathF.Tau;

            int axis = RingAxis(_activeHandle);
            Matrix3 invRot    = Matrix3.Transpose(Matrix3.CreateFromQuaternion(_editStartRotation));
            Vector3 localAxis = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
            Vector3 worldAxis = invRot * localAxis;

            // Flip when axis points away from camera (right-hand rule with Y-down screen)
            if (Vector3.Dot(worldAxis, cam.CameraForward) > 0f) delta = -delta;

            Rotation = _editStartRotation * Quaternion.FromAxisAngle(worldAxis.Normalized(), delta);
            Rotation.Normalize();
        }

        public void EndHandleDrag()  => _activeHandle = HoverNone;
        public bool IsHandleDragging => _activeHandle != HoverNone;

        // Distance from mouse to projected ring arc (for hit-testing)
        public float RingScreenDistance(int axis, int mx, int my, OrbitCamera cam)
        {
            const int N      = 32;
            float     radius = RingRadius;

            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));

            float minDist  = float.MaxValue;
            float prevSx = 0, prevSy = 0;
            bool  prevOk = false;

            for (int j = 0; j <= N; j++)
            {
                float theta = j * MathF.Tau / N;
                float ct = MathF.Cos(theta), st = MathF.Sin(theta);

                Vector3 local = axis switch
                {
                    0 => new Vector3(0, ct, st),
                    1 => new Vector3(ct, 0, st),
                    _ => new Vector3(ct, st, 0),
                } * radius;

                Vector3 world = Center + invRot * local;
                var (sx, sy, behind) = cam.WorldToScreen(world);

                if (!behind && prevOk)
                {
                    float d = PointToSegmentDist(mx, my, prevSx, prevSy, sx, sy);
                    if (d < minDist) minDist = d;
                }
                prevSx = sx; prevSy = sy; prevOk = !behind;
            }
            return minDist;
        }

        private static float PointToSegmentDist(float px, float py,
                                                 float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f) return MathF.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            float t  = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lenSq, 0f, 1f);
            float qx = ax + t * dx - px, qy = ay + t * dy - py;
            return MathF.Sqrt(qx * qx + qy * qy);
        }

        // ────────────────────────────────────────────────────────────────────
        // G/S/R keyboard editing (kept for convenience)
        // ────────────────────────────────────────────────────────────────────

        public void BeginGrab(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction = EditAction.Grab;
            _editStartX = mx; _editStartY = my;
            _editStartCenter = Center;
            _editViewZ = camera.WorldToViewZ(Center);
        }

        public void BeginScale(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction = EditAction.Scale;
            _editStartX = mx;
            _editStartExtents = HalfExtents;
        }

        public void BeginRotate(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction = EditAction.Rotate;
            _editStartX = mx;
            _editStartRotation = Rotation;
        }

        public void UpdateEdit(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            int dx = mx - _editStartX;

            switch (_kbAction)
            {
                case EditAction.Grab:
                {
                    Vector3 s = camera.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 c = camera.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    Vector3 d = c - s;
                    if (_kbAxis >= 0)
                    {
                        Vector3 m = _kbAxis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                        d = m * Vector3.Dot(d, m);
                    }
                    Center = _editStartCenter + d;
                    break;
                }
                case EditAction.Scale:
                {
                    float f = MathF.Max(1f + dx * 0.005f, 0.05f);
                    if (_kbAxis < 0)
                    {
                        HalfExtents = _editStartExtents * f;
                    }
                    else
                    {
                        float v = MathF.Max(_editStartExtents[_kbAxis] * f, 0.01f);
                        HalfExtents = _kbAxis switch
                        {
                            0 => new(v, _editStartExtents.Y, _editStartExtents.Z),
                            1 => new(_editStartExtents.X, v, _editStartExtents.Z),
                            _ => new(_editStartExtents.X, _editStartExtents.Y, v),
                        };
                    }
                    break;
                }
                case EditAction.Rotate:
                {
                    float   angle = dx * 0.5f * MathF.PI / 180f;
                    Vector3 ax    = _kbAxis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                    Rotation = _editStartRotation * Quaternion.FromAxisAngle(ax, angle);
                    Rotation.Normalize();
                    break;
                }
            }
        }

        public void EndEdit() { _kbAction = EditAction.None; _kbAxis = -1; }

        public void AdjustScale(float delta)
        {
            if (!IsEditing) return;
            // In extruding phase: wheel adjusts depth
            if (Phase == ToolPhase.Extruding)
            {
                float halfD = Math.Max(HalfExtents.Z * MathF.Max(1f + delta * 0.15f, 0.05f), 0.001f);
                Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));
                Vector3 worldZ = invRot * Vector3.UnitZ;
                Center      = _screenFaceCenter + worldZ * halfD;
                HalfExtents = new Vector3(HalfExtents.X, HalfExtents.Y, halfD);
                return;
            }
            float fac = MathF.Max(1f + delta * 0.08f, 0.05f);
            if (_kbAxis < 0)
            {
                HalfExtents *= fac;
            }
            else
            {
                float v = MathF.Max(HalfExtents[_kbAxis] * fac, 0.01f);
                HalfExtents = _kbAxis switch
                {
                    0 => new(v, HalfExtents.Y, HalfExtents.Z),
                    1 => new(HalfExtents.X, v, HalfExtents.Z),
                    _ => new(HalfExtents.X, HalfExtents.Y, v),
                };
            }
        }

        public void SetAxisConstraint(int axis) => _kbAxis = axis;

        public void Confirm()
        {
            Phase = ToolPhase.Idle;
        }

        public void Cancel()
        {
            Phase = ToolPhase.Idle;
            HalfExtents = Vector3.Zero;
            _activeHandle = HoverNone;
            _kbAction = EditAction.None;
            _kbAxis   = -1;
        }

        // ────────────────────────────────────────────────────────────────────
        // Selection resolution
        // ────────────────────────────────────────────────────────────────────

        public HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            var result = new HashSet<int>();
            if (HalfExtents.X < 1e-4f || HalfExtents.Y < 1e-4f || HalfExtents.Z < 1e-4f)
                return result;

            Matrix3 rotMat = Matrix3.CreateFromQuaternion(Rotation);
            float hx = HalfExtents.X, hy = HalfExtents.Y, hz = HalfExtents.Z;
            float cx = Center.X,      cy = Center.Y,       cz = Center.Z;

            for (int i = 0; i < points.Length; i++)
            {
                Vector3 local = rotMat * new Vector3(points[i].X - cx, points[i].Y - cy, points[i].Z - cz);
                if (MathF.Abs(local.X) <= hx && MathF.Abs(local.Y) <= hy && MathF.Abs(local.Z) <= hz)
                    result.Add(i);
            }
            return result;
        }

        // ────────────────────────────────────────────────────────────────────
        // Renderer helpers
        // ────────────────────────────────────────────────────────────────────

        public EditAction CurrentAction   => _kbAction;
        public int        AxisConstraint  => _kbAxis;
        public Vector3    ScreenFaceCenter => _screenFaceCenter;

        public float RingRadius => MathF.Max(MathF.Max(HalfExtents.X, HalfExtents.Y), HalfExtents.Z) * 1.3f;

        public Matrix4 GetModelMatrix() =>
            Matrix4.CreateScale(HalfExtents)
          * Matrix4.CreateFromQuaternion(Rotation)
          * Matrix4.CreateTranslation(Center);
    }
}
