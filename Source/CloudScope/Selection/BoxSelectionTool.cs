using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public enum ToolPhase { Idle, Drawing, Editing }

    /// <summary>
    /// 2-phase OBB selection tool.
    ///   Phase 1 – Drawing: left-drag to draw a 2D screen rectangle.
    ///   Phase 2 – Editing: flat box in scene; drag handle 5 (+Z face arrow)
    ///             to extrude into depth; 8 corner handles for resize;
    ///             3 rotation rings (X/Y/Z); center handle to move.
    ///             Camera orbit always works when not over a handle.
    /// </summary>
    public sealed class BoxSelectionTool : ISelectionTool
    {
        public SelectionToolType ToolType => SelectionToolType.Box;

        public bool IsActive  => Phase == ToolPhase.Drawing;
        public bool IsEditing => Phase == ToolPhase.Editing;
        public bool HasVolume => Phase == ToolPhase.Editing && HalfExtents.LengthSquared > 1e-8f;

        public ToolPhase Phase { get; private set; } = ToolPhase.Idle;

        // ── OBB ──────────────────────────────────────────────────────────────
        public Vector3    Center;
        public Vector3    HalfExtents;
        public Quaternion Rotation = Quaternion.Identity;

        // ── Phase 1: Drawing ─────────────────────────────────────────────────
        public int StartX, StartY, EndX, EndY;

        // ── Handle indices ───────────────────────────────────────────────────
        // 0-5:  face centers  (-X,+X,-Y,+Y,-Z,+Z)
        //       handle 5 = +Z face = the "extrude" handle (visually distinct)
        // 6-13: corners
        // 14:   center
        // 15-17: rotation rings (local X, Y, Z)
        public const int HandleCount  = 18;
        public const int HoverNone    = -1;
        public const int ExtrudeHandle = 5;   // +Z face = depth / extrude
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

        public static bool IsFaceHandle(int i)   => i >= 0  && i <= 5;
        public static bool IsCornerHandle(int i)  => i >= 6  && i <= 13;
        public static bool IsCenterHandle(int i)  => i == 14;
        public static bool IsRingHandle(int i)    => i >= 15 && i <= 17;
        public static int  RingAxis(int i)         => i - 15;

        // True when box is still flat (just drawn, not yet extruded)
        public bool IsFlat => HalfExtents.Z < Math.Max(HalfExtents.X, HalfExtents.Y) * 0.05f;

        // ── Edit state ───────────────────────────────────────────────────────
        private int        _activeHandle     = HoverNone;
        private int        _editStartX, _editStartY;
        private float      _editViewZ;
        private Vector3    _editStartCenter;
        private Vector3    _editStartExtents;
        private Quaternion _editStartRotation;
        private float      _ringStartAngle;

        // G/S/R keyboard edit
        private EditAction _kbAction = EditAction.None;
        private int        _kbAxis   = -1;

        // ── Phase 1: Drawing ─────────────────────────────────────────────────

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
            // FinalizeBoxFromScreen called by viewer
        }

        /// <summary>
        /// Build the initial flat OBB from the drawn screen rectangle.
        /// The box is aligned to the camera view; HalfExtents.Z is near-zero.
        /// Handle 5 (+Z) is the extrude handle — drag it to give the box depth.
        /// </summary>
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

            Matrix3 rotMat = new Matrix3(axisX, axisY, axisZ);
            Rotation = Quaternion.FromMatrix(rotMat);
            Rotation.Normalize();

            // Start flat: very thin in Z so the extrude handle is clearly on the drawn plane
            float thinZ = Math.Max(halfW, halfH) * 0.002f;
            Center      = cam.ScreenToWorldAtDepth(cxs, cys, refViewZ);
            HalfExtents = new Vector3(halfW, halfH, thinZ);
            Phase       = ToolPhase.Editing;
        }

        // ── Phase 2: Handle interaction ──────────────────────────────────────

        public int HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f)
        {
            if (Phase != ToolPhase.Editing) return HoverNone;

            int   best     = HoverNone;
            float bestDist = threshold;

            // Face handles (0-5): test the entire arrow shaft (base → tip)
            for (int i = 0; i < 6; i++)
            {
                var (fx, fy, fb) = cam.WorldToScreen(HandleWorldPosition(i));
                var (tx, ty, tb) = cam.WorldToScreen(FaceArrowTipWorldPosition(i));
                if (fb && tb) continue;
                float d = SegDist(mx, my, fx, fy, tx, ty);
                if (d < bestDist) { bestDist = d; best = i; }
            }

            // Corner / center handles (6-14): single-point hit-test
            for (int i = 6; i < 15; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(HandleWorldPosition(i));
                if (behind) continue;
                float d = MathF.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (d < bestDist) { bestDist = d; best = i; }
            }

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
            Vector4 wp = new Vector4(HandleLocalPos[i], 1f) * GetModelMatrix();
            return wp.Xyz;
        }

        public float ArrowWorldLength =>
            MathF.Max(MathF.Max(HalfExtents.X, HalfExtents.Y), HalfExtents.Z) * 0.22f;

        public Vector3 FaceArrowTipWorldPosition(int i)
        {
            Vector3 facePos  = HandleWorldPosition(i);
            Matrix3 invRot   = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));
            Vector3 worldDir = (invRot * HandleLocalPos[i]).Normalized();
            return facePos + worldDir * MathF.Max(ArrowWorldLength, 0.01f);
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

                float axisDelta  = faceAxis switch { 0 => localDelta.X, 1 => localDelta.Y, _ => localDelta.Z } * sign;
                float newExtent  = Math.Max(_editStartExtents[faceAxis] + axisDelta * 0.5f, 0.001f);
                float extChange  = newExtent - _editStartExtents[faceAxis];

                HalfExtents = faceAxis switch
                {
                    0 => new Vector3(newExtent, _editStartExtents.Y, _editStartExtents.Z),
                    1 => new Vector3(_editStartExtents.X, newExtent, _editStartExtents.Z),
                    _ => new Vector3(_editStartExtents.X, _editStartExtents.Y, newExtent),
                };

                Vector3 localAxis = faceAxis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                Center = _editStartCenter + invRotMat * localAxis * (sign * extChange);
            }
            else // corner
            {
                Vector3 cs = HandleLocalPos[_activeHandle];
                Vector3 de = new Vector3(localDelta.X * cs.X, localDelta.Y * cs.Y, localDelta.Z * cs.Z);

                Vector3 newExt = new Vector3(
                    Math.Max(_editStartExtents.X + de.X * 0.5f, 0.001f),
                    Math.Max(_editStartExtents.Y + de.Y * 0.5f, 0.001f),
                    Math.Max(_editStartExtents.Z + de.Z * 0.5f, 0.001f));

                Vector3 shift = newExt - _editStartExtents;
                HalfExtents   = newExt;
                Center        = _editStartCenter + invRotMat * new Vector3(cs.X * shift.X, cs.Y * shift.Y, cs.Z * shift.Z);
            }
        }

        private void UpdateRingDrag(int mx, int my, OrbitCamera cam)
        {
            var (cx, cy, _) = cam.WorldToScreen(Center);
            float cur   = MathF.Atan2(my - cy, mx - cx);
            float delta = cur - _ringStartAngle;
            while (delta >  MathF.PI) delta -= MathF.Tau;
            while (delta < -MathF.PI) delta += MathF.Tau;

            int     axis      = RingAxis(_activeHandle);
            Matrix3 invRot    = Matrix3.Transpose(Matrix3.CreateFromQuaternion(_editStartRotation));
            Vector3 localAxis = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
            Vector3 worldAxis = invRot * localAxis;

            if (Vector3.Dot(worldAxis, cam.CameraForward) > 0f) delta = -delta;

            Rotation = _editStartRotation * Quaternion.FromAxisAngle(worldAxis.Normalized(), delta);
            Rotation.Normalize();
        }

        // Hit-test a rotation ring — returns screen-space distance to nearest arc segment
        public float RingScreenDistance(int axis, int mx, int my, OrbitCamera cam)
        {
            const int N      = 32;
            float     radius = RingRadius;
            Matrix3   invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));

            float minDist  = float.MaxValue;
            float prevSx = 0, prevSy = 0;
            bool  prevOk   = false;

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

                var (sx, sy, behind) = cam.WorldToScreen(Center + invRot * local);

                if (!behind && prevOk)
                {
                    float d = SegDist(mx, my, prevSx, prevSy, sx, sy);
                    if (d < minDist) minDist = d;
                }
                prevSx = sx; prevSy = sy; prevOk = !behind;
            }
            return minDist;
        }

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f) return MathF.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            float t  = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lenSq, 0f, 1f);
            float qx = ax + t * dx - px, qy = ay + t * dy - py;
            return MathF.Sqrt(qx * qx + qy * qy);
        }

        public void EndHandleDrag()  => _activeHandle = HoverNone;
        public bool IsHandleDragging => _activeHandle != HoverNone;

        // ── G/S/R keyboard editing ───────────────────────────────────────────

        public void BeginGrab(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction = EditAction.Grab; _editStartX = mx; _editStartY = my;
            _editStartCenter = Center; _editViewZ = camera.WorldToViewZ(Center);
        }

        public void BeginScale(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction = EditAction.Scale; _editStartX = mx; _editStartExtents = HalfExtents;
        }

        public void BeginRotate(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction = EditAction.Rotate; _editStartX = mx; _editStartRotation = Rotation;
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
                    if (_kbAxis >= 0) { Vector3 m = _kbAxis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ }; d = m * Vector3.Dot(d, m); }
                    Center = _editStartCenter + d;
                    break;
                }
                case EditAction.Scale:
                {
                    float f = MathF.Max(1f + dx * 0.005f, 0.05f);
                    HalfExtents = _kbAxis < 0 ? _editStartExtents * f : _editStartExtents;
                    if (_kbAxis >= 0)
                    {
                        float v = MathF.Max(_editStartExtents[_kbAxis] * f, 0.001f);
                        HalfExtents = _kbAxis switch { 0 => new Vector3(v, _editStartExtents.Y, _editStartExtents.Z), 1 => new Vector3(_editStartExtents.X, v, _editStartExtents.Z), _ => new Vector3(_editStartExtents.X, _editStartExtents.Y, v) };
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
            float fac = MathF.Max(1f + delta * 0.08f, 0.05f);
            if (_kbAxis < 0) { HalfExtents *= fac; return; }
            float v = MathF.Max(HalfExtents[_kbAxis] * fac, 0.001f);
            HalfExtents = _kbAxis switch { 0 => new Vector3(v, HalfExtents.Y, HalfExtents.Z), 1 => new Vector3(HalfExtents.X, v, HalfExtents.Z), _ => new Vector3(HalfExtents.X, HalfExtents.Y, v) };
        }

        public void SetAxisConstraint(int axis) => _kbAxis = axis;

        public void Confirm() { Phase = ToolPhase.Idle; HalfExtents = Vector3.Zero; }

        public void Cancel()
        {
            Phase = ToolPhase.Idle; HalfExtents = Vector3.Zero;
            _activeHandle = HoverNone; _kbAction = EditAction.None; _kbAxis = -1;
        }

        // ── Selection resolution ─────────────────────────────────────────────

        public HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            if (HalfExtents.X < 1e-4f || HalfExtents.Y < 1e-4f || HalfExtents.Z < 1e-4f)
                return new HashSet<int>();

            // Inline the rotation matrix elements to avoid per-point struct overhead.
            // Matrix3.CreateFromQuaternion gives the world→local transform (OpenTK column convention).
            Matrix3 m = Matrix3.CreateFromQuaternion(Rotation);
            float m00 = m.M11, m01 = m.M12, m02 = m.M13;
            float m10 = m.M21, m11 = m.M22, m12 = m.M23;
            float m20 = m.M31, m21 = m.M32, m22 = m.M33;
            float hx = HalfExtents.X, hy = HalfExtents.Y, hz = HalfExtents.Z;
            float cx = Center.X, cy = Center.Y, cz = Center.Z;

            // Collect into a List first (no per-add bucket resize overhead), then wrap in HashSet.
            var list = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                float dx = points[i].X - cx;
                float dy = points[i].Y - cy;
                float dz = points[i].Z - cz;
                // OpenTK Matrix3 * Vector3: result = columns-of-M dotted with v
                float lx = m00 * dx + m10 * dy + m20 * dz;
                if (MathF.Abs(lx) > hx) continue;
                float ly = m01 * dx + m11 * dy + m21 * dz;
                if (MathF.Abs(ly) > hy) continue;
                float lz = m02 * dx + m12 * dy + m22 * dz;
                if (MathF.Abs(lz) <= hz) list.Add(i);
            }
            return new HashSet<int>(list);
        }

        // ── Renderer helpers ─────────────────────────────────────────────────

        public EditAction CurrentAction  => _kbAction;
        public int        AxisConstraint  => _kbAxis;
        public float      RingRadius      => MathF.Max(MathF.Max(HalfExtents.X, HalfExtents.Y), HalfExtents.Z) * 1.3f;

        public Matrix4 GetModelMatrix() =>
            Matrix4.CreateScale(HalfExtents)
          * Matrix4.CreateFromQuaternion(Rotation)
          * Matrix4.CreateTranslation(Center);
    }
}
