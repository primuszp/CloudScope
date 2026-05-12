using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// 2-phase OBB selection tool.
    ///   Drawing  – left-drag to draw a 2D screen rectangle.
    ///   Editing  – flat box in scene; drag handle 5 (+Z face) to extrude;
    ///              8 corners for resize; 3 rotation rings; center handle to move.
    /// </summary>
    public sealed class BoxSelectionTool : SelectionToolBase
    {
        public override SelectionToolType ToolType => SelectionToolType.Box;
        public override int HandleCount => 18;
        public override IReadOnlyList<GripDescriptor> Grips => GripLayout;

        public override bool HasVolume =>
            Phase == ToolPhase.Editing && HalfExtents.LengthSquared > 1e-8f;

        // ── OBB ──────────────────────────────────────────────────────────────
        public Vector3    HalfExtents;
        public Quaternion Rotation = Quaternion.Identity;

        // ── Phase 1: Drawing ─────────────────────────────────────────────────
        public int StartX, StartY, EndX, EndY;

        // ── Handle indices ───────────────────────────────────────────────────
        // 0-5:  face centers  (-X,+X,-Y,+Y,-Z,+Z)
        //       handle 5 = +Z face = "extrude" handle
        // 6-13: corners
        // 14:   center
        // 15-17: rotation rings (local X, Y, Z)
        public const int ExtrudeHandle = 5;
        private static readonly GripDescriptor[] GripLayout =
        {
            new(0, GripKind.AxisResize, 0, -1),
            new(1, GripKind.AxisResize, 0,  1),
            new(2, GripKind.AxisResize, 1, -1),
            new(3, GripKind.AxisResize, 1,  1),
            new(4, GripKind.AxisResize, 2, -1),
            new(5, GripKind.AxisResize, 2,  1, true),
            new(6, GripKind.CornerResize),
            new(7, GripKind.CornerResize),
            new(8, GripKind.CornerResize),
            new(9, GripKind.CornerResize),
            new(10, GripKind.CornerResize),
            new(11, GripKind.CornerResize),
            new(12, GripKind.CornerResize),
            new(13, GripKind.CornerResize),
            new(14, GripKind.Center),
            new(15, GripKind.RotationRing, 0),
            new(16, GripKind.RotationRing, 1),
            new(17, GripKind.RotationRing, 2),
        };

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

        public static bool IsFaceHandle(int i)   => GripLayout[i].Kind == GripKind.AxisResize;
        public static bool IsCornerHandle(int i) => GripLayout[i].Kind == GripKind.CornerResize;
        public static bool IsCenterHandle(int i) => GripLayout[i].Kind == GripKind.Center;
        public static bool IsRingHandle(int i)   => GripLayout[i].Kind == GripKind.RotationRing;
        public static int  RingAxis(int i)       => GripLayout[i].Axis;

        public bool IsFlat => HalfExtents.Z < Math.Max(HalfExtents.X, HalfExtents.Y) * 0.05f;

        // ── Box-specific edit state ───────────────────────────────────────────
        private Vector3    _editStartExtents;
        private Quaternion _editStartRotation;

        // ── Phase 1: Drawing ─────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            StartX = EndX = mx;
            StartY = EndY = my;
            Phase = ToolPhase.Drawing;
            HalfExtents = Vector3.Zero;
            Rotation = Quaternion.Identity;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (Phase == ToolPhase.Drawing) { EndX = mx; EndY = my; }
        }

        public override void OnMouseUp(int mx, int my, OrbitCamera camera)
        {
            if (Phase != ToolPhase.Drawing) return;
            EndX = mx; EndY = my;
            if (Math.Abs(EndX - StartX) < 5 && Math.Abs(EndY - StartY) < 5)
            {
                Phase = ToolPhase.Idle;
                return;
            }

            FinalizeBoxFromScreen(camera);
        }

        /// <summary>Build the initial flat OBB from the drawn screen rectangle.</summary>
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

            // OpenTK: CreateFromQuaternion(FromMatrix(M)) = M^T — feed transpose
            Matrix3 rotMat = new Matrix3(axisX, axisY, axisZ);
            Rotation = Quaternion.FromMatrix(Matrix3.Transpose(rotMat));
            Rotation.Normalize();

            float thinZ = Math.Max(halfW, halfH) * 0.002f;
            Center      = cam.ScreenToWorldAtDepth(cxs, cys, refViewZ);
            HalfExtents = new Vector3(halfW, halfH, thinZ);
            Phase       = ToolPhase.Editing;
        }

        // ── Handle interaction ────────────────────────────────────────────────

        protected override float GetGripHitDistance(GripDescriptor grip, int mx, int my, OrbitCamera cam)
        {
            if (grip.Kind == GripKind.AxisResize)
            {
                var (fx, fy, fb) = cam.WorldToScreen(HandleWorldPosition(grip.Index));
                var (tx, ty, tb) = cam.WorldToScreen(FaceArrowTipWorldPosition(grip.Index));
                if (fb && tb)
                    return float.MaxValue;

                return GripInteractionMath.SegmentDistance(mx, my, fx, fy, tx, ty);
            }

            if (grip.Kind == GripKind.RotationRing)
                return RingScreenDistance(grip.Axis, mx, my, cam);

            return base.GetGripHitDistance(grip, mx, my, cam);
        }

        public override Vector3 HandleWorldPosition(int i)
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

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartExtents  = HalfExtents;
            _editStartRotation = Rotation;
            // _ringStartAngle unused — ring drag uses _editStartX/Y directly
        }

        public override void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle == HoverNone) return;

            if (IsRingHandle(_activeHandle))
            {
                UpdateRingDrag(mx, my, cam);
                return;
            }

            Vector3 worldDelta = GripInteractionMath.ComputeWorldDragDelta(
                cam,
                _editStartX,
                _editStartY,
                mx,
                my,
                _editViewZ);

            if (IsCenterHandle(_activeHandle))
            {
                Center = _editStartCenter + worldDelta;
                return;
            }

            Matrix3 rotMat    = Matrix3.CreateFromQuaternion(Rotation);
            Matrix3 invRotMat = Matrix3.Transpose(rotMat);
            Vector3 localDelta = rotMat * worldDelta;

            if (IsFaceHandle(_activeHandle))
            {
                GripDescriptor grip = GetGrip(_activeHandle);
                int   faceAxis  = grip.Axis;
                float sign      = grip.Sign;
                float axisDelta = faceAxis switch { 0 => localDelta.X, 1 => localDelta.Y, _ => localDelta.Z } * sign;
                float newExtent = Math.Max(_editStartExtents[faceAxis] + axisDelta * 0.5f, 0.001f);
                float extChange = newExtent - _editStartExtents[faceAxis];

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
            Rotation = GripInteractionMath.RotateAroundRingDrag(
                cam,
                Center,
                _editStartRotation,
                RingAxis(_activeHandle),
                _editStartX,
                _editStartY,
                mx,
                my);
        }

        public float RingScreenDistance(int axis, int mx, int my, OrbitCamera cam)
        {
            return GripInteractionMath.RingScreenDistance(cam, Center, Rotation, axis, RingRadius, mx, my);
        }

        // ── Keyboard edit overrides ───────────────────────────────────────────

        protected override void OnBeginScaleExtra(int mx, int my, OrbitCamera cam)
            => _editStartExtents = HalfExtents;

        public override void BeginRotate(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction          = EditAction.Rotate;
            _editStartX        = mx;
            _editStartRotation = Rotation;
        }

        protected override void UpdateEditShape(EditAction action, int mx, int my, OrbitCamera cam)
        {
            int dx = mx - _editStartX;
            switch (action)
            {
                case EditAction.Scale:
                {
                    float f = MathF.Max(1f + dx * MouseDragSensitivity, 0.05f);
                    if (_kbAxis < 0)
                    {
                        HalfExtents = _editStartExtents * f;
                    }
                    else
                    {
                        float v = MathF.Max(_editStartExtents[_kbAxis] * f, 0.001f);
                        HalfExtents = _kbAxis switch
                        {
                            0 => new Vector3(v, _editStartExtents.Y, _editStartExtents.Z),
                            1 => new Vector3(_editStartExtents.X, v, _editStartExtents.Z),
                            _ => new Vector3(_editStartExtents.X, _editStartExtents.Y, v),
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

        public override void AdjustScale(float delta)
        {
            if (!IsEditing) return;
            float fac = MathF.Max(1f + delta * ScrollScaleFactor, 0.05f);
            if (_kbAxis < 0) { HalfExtents *= fac; return; }
            float v = MathF.Max(HalfExtents[_kbAxis] * fac, 0.001f);
            HalfExtents = _kbAxis switch
            {
                0 => new Vector3(v, HalfExtents.Y, HalfExtents.Z),
                1 => new Vector3(HalfExtents.X, v, HalfExtents.Z),
                _ => new Vector3(HalfExtents.X, HalfExtents.Y, v),
            };
        }

        public override void Confirm()
        {
            base.Confirm();
            HalfExtents = Vector3.Zero;
        }

        public override void Cancel()
        {
            base.Cancel();
            HalfExtents = Vector3.Zero;
        }

        // ── Selection resolution ─────────────────────────────────────────────

        public override IPointSelectionQuery CreateQuery()
            => new BoxSelectionQuery(Center, HalfExtents, Rotation);

        private sealed class BoxSelectionQuery : IPointSelectionQuery
        {
            private readonly float _r00, _r01, _r02;
            private readonly float _r10, _r11, _r12;
            private readonly float _r20, _r21, _r22;
            private readonly float _hx, _hy, _hz;
            private readonly float _cx, _cy, _cz;

            public BoxSelectionQuery(Vector3 center, Vector3 halfExtents, Quaternion rotation)
            {
                Matrix3 r = Matrix3.CreateFromQuaternion(rotation);
                _r00 = r.M11; _r01 = r.M12; _r02 = r.M13;
                _r10 = r.M21; _r11 = r.M22; _r12 = r.M23;
                _r20 = r.M31; _r21 = r.M32; _r22 = r.M33;
                _hx = halfExtents.X; _hy = halfExtents.Y; _hz = halfExtents.Z;
                _cx = center.X; _cy = center.Y; _cz = center.Z;
            }

            public IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default)
            {
                if (_hx < 1e-4f || _hy < 1e-4f || _hz < 1e-4f)
                    return Array.Empty<int>();

                var list = new List<int>();
                for (int i = 0; i < points.Length; i++)
                {
                    if ((i & 4095) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    float dx = points[i].X - _cx;
                    float dy = points[i].Y - _cy;
                    float dz = points[i].Z - _cz;
                    float lx = _r00 * dx + _r01 * dy + _r02 * dz;
                    if (MathF.Abs(lx) > _hx) continue;
                    float ly = _r10 * dx + _r11 * dy + _r12 * dz;
                    if (MathF.Abs(ly) > _hy) continue;
                    float lz = _r20 * dx + _r21 * dy + _r22 * dz;
                    if (MathF.Abs(lz) <= _hz) list.Add(i);
                }

                return list;
            }
        }

        // ── Renderer helpers ─────────────────────────────────────────────────

        public float RingRadius =>
            MathF.Max(MathF.Max(HalfExtents.X, HalfExtents.Y), HalfExtents.Z) * 1.3f;

        public Matrix4 GetModelMatrix() =>
            Matrix4.CreateScale(HalfExtents)
          * Matrix4.CreateFromQuaternion(Rotation)
          * Matrix4.CreateTranslation(Center);
    }
}
