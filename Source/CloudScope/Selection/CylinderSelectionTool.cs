using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space upright cylinder (axis = world Z) — two-phase workflow.
    ///
    /// Phase 1 – Drawing:
    ///   Click sets center on the point cloud, drag sets radius (world-space distance).
    ///   Release → thin disk placed, Phase = Editing.
    ///
    /// Phase 2 – Editing:
    ///   10 handles:
    ///     0  = center        (move)
    ///     1  = top cap       (extrude arrow — drag to set height)
    ///     2  = bottom cap    (drag to lower base)
    ///     3  = +local-X radial,   4 = -local-X radial
    ///     5  = +local-Z radial,   6 = -local-Z radial
    ///     7  = X-rotation ring,   8 = Y-rotation ring,   9 = Z-rotation ring
    ///   G/S/R + X/Y/Z axis constraints, scroll = fine-tune.
    /// </summary>
    public sealed class CylinderSelectionTool : SelectionToolBase
    {
        public override SelectionToolType ToolType    => SelectionToolType.Cylinder;
        public override int               HandleCount => 10;

        public override bool HasVolume =>
            IsEditing && Radius > 1e-4f && HalfHeight > 1e-4f;

        public float      Radius     { get; set; }
        public float      HalfHeight { get; set; }
        public Quaternion Rotation   = DefaultRotation;

        // Rotate 90° around X: local Y → world Z (upright, Z is up in LiDAR coordinates)
        private static readonly Quaternion DefaultRotation = Quaternion.Identity;

        // Local axes derived from rotation
        public  Vector3 Axis   => Vector3.Transform(Vector3.UnitZ, Rotation);
        private Vector3 LocalX => Vector3.Transform(Vector3.UnitX, Rotation);
        private Vector3 LocalY => Vector3.Transform(Vector3.UnitY, Rotation);

        // Ring radius: slightly larger than the cylinder for visual clarity
        public float RingRadius => MathF.Max(Radius, HalfHeight) * 1.35f;

        public static bool IsRingHandle(int i) => i >= 7 && i <= 9;
        public static int  RingAxis(int i)     => i - 7;

        // ── Drag/edit state ───────────────────────────────────────────────────
        private float      _editStartRadius;
        private float      _editStartHalfHeight;
        private Quaternion _editStartRotation;
        private Vector2    _radialScreenDir;

        // ── Phase 1: Placement ────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 11);
            Center     = worldPt;
            Radius     = 0f;
            HalfHeight = 0f;
            Rotation   = DefaultRotation;
            Phase      = ToolPhase.Drawing;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 11);
            // Horizontal distance in XY plane (Z is up)
            float dx = worldPt.X - Center.X;
            float dy = worldPt.Y - Center.Y;
            Radius = MathF.Max(MathF.Sqrt(dx * dx + dy * dy), 0f);
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (!IsActive) return;
            if (Radius < 0.01f) { Phase = ToolPhase.Idle; return; }
            HalfHeight = Radius;   // default: cube-ish proportions, user extrudes from here
            Phase      = ToolPhase.Editing;
        }

        // ── Handle positions ──────────────────────────────────────────────────

        public override Vector3 HandleWorldPosition(int i) => i switch
        {
            0 => Center,
            1 => Center + Axis *  HalfHeight,
            2 => Center - Axis *  HalfHeight,
            3 => Center + LocalX *  Radius,
            4 => Center + LocalX * -Radius,
            5 => Center + LocalY *  Radius,
            6 => Center + LocalY * -Radius,
            _ => Center  // rings 7-9: return center (hit-tested separately)
        };

        // ── Handle hit test (overrides base to add ring proximity test) ───────

        public override int HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f)
        {
            if (Phase != ToolPhase.Editing) return HoverNone;

            int   best     = HoverNone;
            float bestDist = threshold;

            // Point handles 0-6
            for (int i = 0; i < 7; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(HandleWorldPosition(i));
                if (behind) continue;
                float d = MathF.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (d < bestDist) { bestDist = d; best = i; }
            }

            // Ring handles 7-9
            for (int r = 0; r < 3; r++)
            {
                float d = RingScreenDist(r, mx, my, cam);
                if (d < bestDist) { bestDist = d; best = 7 + r; }
            }

            return best;
        }

        private float RingScreenDist(int axis, int mx, int my, OrbitCamera cam)
        {
            const int N    = 32;
            float     rad  = RingRadius;
            Matrix3   invR = Matrix3.Transpose(Matrix3.CreateFromQuaternion(Rotation));

            float minDist = float.MaxValue;
            float psx = 0, psy = 0;
            bool  pok  = false;

            for (int j = 0; j <= N; j++)
            {
                float t = j * MathF.Tau / N;
                float ct = MathF.Cos(t), st = MathF.Sin(t);
                Vector3 local = axis switch
                {
                    0 => new Vector3(0f, ct, st),
                    1 => new Vector3(ct, 0f, st),
                    _ => new Vector3(ct, st, 0f),
                } * rad;

                var (sx, sy, behind) = cam.WorldToScreen(Center + invR * local);
                if (!behind && pok)
                {
                    float ddx = sx - psx, ddy = sy - psy;
                    float lenSq = ddx * ddx + ddy * ddy;
                    float t2 = lenSq < 1e-6f ? 0f :
                        Math.Clamp(((mx - psx) * ddx + (my - psy) * ddy) / lenSq, 0f, 1f);
                    float qx = psx + t2 * ddx - mx, qy = psy + t2 * ddy - my;
                    float d  = MathF.Sqrt(qx * qx + qy * qy);
                    if (d < minDist) minDist = d;
                }
                psx = sx; psy = sy; pok = !behind;
            }
            return minDist;
        }

        // ── Handle drag ───────────────────────────────────────────────────────

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;
            _editStartRotation   = Rotation;

            if (handle >= 3 && handle <= 6)
            {
                var (cx, cy, _) = cam.WorldToScreen(Center);
                var (px, py, _) = cam.WorldToScreen(HandleWorldPosition(handle));
                float dx = px - cx, dy = py - cy;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                _radialScreenDir = len > 0.5f
                    ? new Vector2(dx / len, dy / len)
                    : new Vector2(1f, 0f);
            }
        }

        public override void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle < 0) return;

            if (IsRingHandle(_activeHandle))
            {
                UpdateRingDrag(mx, my, cam);
                return;
            }

            switch (_activeHandle)
            {
                case 0:
                {
                    Vector3 s = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 c = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    Center = _editStartCenter + (c - s);
                    break;
                }
                case 1:  // Top cap — fix bottom, move top only
                {
                    Vector3 s    = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 c    = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    float   proj = Vector3.Dot(c - s, Axis);
                    // Fixed end = bottom at drag start
                    Vector3 fixedEnd  = _editStartCenter - Axis * _editStartHalfHeight;
                    float   newHeight = MathF.Max(_editStartHalfHeight * 2f + proj, 0.02f);
                    HalfHeight = newHeight * 0.5f;
                    Center     = fixedEnd + Axis * HalfHeight;
                    break;
                }
                case 2:  // Bottom cap — fix top, move bottom only
                {
                    Vector3 s    = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 c    = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    float   proj = Vector3.Dot(c - s, Axis);
                    // Fixed end = top at drag start
                    Vector3 fixedEnd  = _editStartCenter + Axis * _editStartHalfHeight;
                    float   newHeight = MathF.Max(_editStartHalfHeight * 2f - proj, 0.02f);
                    HalfHeight = newHeight * 0.5f;
                    Center     = fixedEnd - Axis * HalfHeight;
                    break;
                }
                default: // Radial handles 3-6
                {
                    float proj = (mx - _editStartX) * _radialScreenDir.X
                               + (my - _editStartY) * _radialScreenDir.Y;
                    Radius = MathF.Max(_editStartRadius * (1f + proj * MouseDragSensitivity), 0.01f);
                    break;
                }
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

        // ── Keyboard editing ──────────────────────────────────────────────────

        public override void BeginRotate(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction          = EditAction.Rotate;
            _editStartX        = mx;
            _editStartRotation = Rotation;
        }

        protected override void OnBeginScaleExtra(int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;
        }

        protected override void UpdateEditShape(EditAction action, int mx, int my, OrbitCamera cam)
        {
            int dx = mx - _editStartX;
            switch (action)
            {
                case EditAction.Scale:
                {
                    float f = 1f + dx * MouseDragSensitivity;
                    if      (_kbAxis == 2)                 HalfHeight = MathF.Max(_editStartHalfHeight * f, 0.01f);
                    else if (_kbAxis == 0 || _kbAxis == 1) Radius     = MathF.Max(_editStartRadius * f, 0.01f);
                    else
                    {
                        Radius     = MathF.Max(_editStartRadius     * f, 0.01f);
                        HalfHeight = MathF.Max(_editStartHalfHeight * f, 0.01f);
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
            float fac = 1f + delta * ScrollScaleFactor;
            Radius     = MathF.Max(Radius     * fac, 0.01f);
            HalfHeight = MathF.Max(HalfHeight * fac, 0.01f);
        }

        public override void Cancel()
        {
            base.Cancel();
            Radius     = 0f;
            HalfHeight = 0f;
            Rotation   = DefaultRotation;
        }

        // ── Resolution — fast dot-product path, no per-point quaternion ───────

        public override HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH, CancellationToken ct = default)
        {
            if (Radius < 1e-4f || HalfHeight < 1e-4f) return new HashSet<int>();

            float   r2 = Radius * Radius;
            float   hh = HalfHeight;
            float   cx = Center.X, cy = Center.Y, cz = Center.Z;
            Vector3 ax = Axis;

            var list = new List<int>(capacity: 256);
            for (int i = 0; i < points.Length; i++)
            {
                if ((i & 0xFFFF) == 0 && ct.IsCancellationRequested) return new HashSet<int>();

                float dx = points[i].X - cx;
                float dy = points[i].Y - cy;
                float dz = points[i].Z - cz;

                float h       = dx * ax.X + dy * ax.Y + dz * ax.Z;
                if (MathF.Abs(h) > hh) continue;

                float radial2 = dx * dx + dy * dy + dz * dz - h * h;
                if (radial2 <= r2) list.Add(i);
            }
            return new HashSet<int>(list);
        }
    }
}
