using System;
using System.Collections.Generic;
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

        public override void OnMouseUp(int mx, int my)
        {
            if (Phase != ToolPhase.Drawing) return;
            EndX = mx; EndY = my;
            if (Math.Abs(EndX - StartX) < 5 && Math.Abs(EndY - StartY) < 5)
                Phase = ToolPhase.Idle;
            // FinalizeBoxFromScreen called by viewer after this
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

        public override int HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f)
        {
            if (Phase != ToolPhase.Editing) return HoverNone;

            int   best     = HoverNone;
            float bestDist = threshold;

            // Face handles: test the full arrow shaft (base → tip)
            for (int i = 0; i < 6; i++)
            {
                var (fx, fy, fb) = cam.WorldToScreen(HandleWorldPosition(i));
                var (tx, ty, tb) = cam.WorldToScreen(FaceArrowTipWorldPosition(i));
                if (fb && tb) continue;
                float d = SegDist(mx, my, fx, fy, tx, ty);
                if (d < bestDist) { bestDist = d; best = i; }
            }

            // Corner + center: single-point hit
            for (int i = 6; i < 15; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(HandleWorldPosition(i));
                if (behind) continue;
                float d = MathF.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (d < bestDist) { bestDist = d; best = i; }
            }

            // Rings: arc proximity
            for (int r = 0; r < 3; r++)
            {
                float d = RingScreenDistance(r, mx, my, cam);
                if (d < bestDist) { bestDist = d; best = 15 + r; }
            }

            return best;
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

            Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
            Vector3 curWorld   = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
            Vector3 worldDelta = curWorld - startWorld;

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
                int   faceAxis  = _activeHandle / 2;
                float sign      = (_activeHandle % 2 == 0) ? -1f : 1f;
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
            int     axis      = RingAxis(_activeHandle);
            Matrix3 invRot    = Matrix3.Transpose(Matrix3.CreateFromQuaternion(_editStartRotation));
            Vector3 localAxis = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
            Vector3 worldAxis = (invRot * localAxis).Normalized();

            float   viewZ = cam.WorldToViewZ(Center);
            Vector3 p0    = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, viewZ);
            Vector3 p1    = cam.ScreenToWorldAtDepth(mx, my, viewZ);

            Vector3 v0 = p0 - Center; v0 -= Vector3.Dot(v0, worldAxis) * worldAxis;
            Vector3 v1 = p1 - Center; v1 -= Vector3.Dot(v1, worldAxis) * worldAxis;
            if (v0.LengthSquared < 1e-8f || v1.LengthSquared < 1e-8f) return;

            v0 = v0.Normalized(); v1 = v1.Normalized();
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(v0, v1), -1f, 1f));
            if (Vector3.Dot(Vector3.Cross(v0, v1), worldAxis) < 0f) angle = -angle;

            Rotation = Quaternion.FromAxisAngle(worldAxis, angle) * _editStartRotation;
            Rotation.Normalize();
        }

        public float RingScreenDistance(int axis, int mx, int my, OrbitCamera cam)
        {
            const int N      = 32;
            float     radius = RingRadius;
            Matrix3   invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));

            float minDist = float.MaxValue;
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

        public override HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            if (HalfExtents.X < 1e-4f || HalfExtents.Y < 1e-4f || HalfExtents.Z < 1e-4f)
                return new HashSet<int>();

            Matrix3 r = Matrix3.CreateFromQuaternion(Rotation);
            float r00 = r.M11, r01 = r.M12, r02 = r.M13;
            float r10 = r.M21, r11 = r.M22, r12 = r.M23;
            float r20 = r.M31, r21 = r.M32, r22 = r.M33;
            float hx = HalfExtents.X, hy = HalfExtents.Y, hz = HalfExtents.Z;
            float cx = Center.X, cy = Center.Y, cz = Center.Z;

            var list = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                float dx = points[i].X - cx;
                float dy = points[i].Y - cy;
                float dz = points[i].Z - cz;
                float lx = r00 * dx + r01 * dy + r02 * dz;
                if (MathF.Abs(lx) > hx) continue;
                float ly = r10 * dx + r11 * dy + r12 * dz;
                if (MathF.Abs(ly) > hy) continue;
                float lz = r20 * dx + r21 * dy + r22 * dz;
                if (MathF.Abs(lz) <= hz) list.Add(i);
            }
            return new HashSet<int>(list);
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
