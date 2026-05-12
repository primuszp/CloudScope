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
        public override IReadOnlyList<GripDescriptor> Grips => GripLayout;

        public override bool HasVolume =>
            IsEditing && Radius > 1e-4f && HalfHeight > 1e-4f;

        public float      Radius     { get; set; }
        public float      HalfHeight { get; set; }
        public Quaternion Rotation   = DefaultRotation;
        private static readonly GripDescriptor[] GripLayout =
        {
            new(0, GripKind.Center),
            new(1, GripKind.HeightResize, 2,  1, true),
            new(2, GripKind.HeightResize, 2, -1),
            new(3, GripKind.RadiusResize, 0,  1),
            new(4, GripKind.RadiusResize, 0, -1),
            new(5, GripKind.RadiusResize, 1,  1),
            new(6, GripKind.RadiusResize, 1, -1),
            new(7, GripKind.RotationRing, 0),
            new(8, GripKind.RotationRing, 1),
            new(9, GripKind.RotationRing, 2),
        };

        // Rotate 90° around X: local Y → world Z (upright, Z is up in LiDAR coordinates)
        private static readonly Quaternion DefaultRotation = Quaternion.Identity;

        // Local axes derived from rotation
        public  Vector3 Axis   => Vector3.Transform(Vector3.UnitZ, Rotation);
        private Vector3 LocalX => Vector3.Transform(Vector3.UnitX, Rotation);
        private Vector3 LocalY => Vector3.Transform(Vector3.UnitY, Rotation);

        // Ring radius: slightly larger than the cylinder for visual clarity
        public float RingRadius => MathF.Max(Radius, HalfHeight) * 1.35f;

        public static bool IsRingHandle(int i) => GripLayout[i].Kind == GripKind.RotationRing;
        public static int  RingAxis(int i)     => GripLayout[i].Axis;

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

        public override void OnMouseUp(int mx, int my, OrbitCamera camera)
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

        protected override float GetGripHitDistance(GripDescriptor grip, int mx, int my, OrbitCamera cam)
        {
            if (grip.Kind == GripKind.RotationRing)
                return GripInteractionMath.RingScreenDistance(cam, Center, Rotation, grip.Axis, RingRadius, mx, my);

            return base.GetGripHitDistance(grip, mx, my, cam);
        }

        // ── Handle drag ───────────────────────────────────────────────────────

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;
            _editStartRotation   = Rotation;

            if (GetGrip(handle).Kind == GripKind.RadiusResize)
            {
                _radialScreenDir = GripInteractionMath.ComputeScreenDirection(cam, Center, HandleWorldPosition(handle));
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

            switch (GetGrip(_activeHandle).Kind)
            {
                case GripKind.Center:
                {
                    Center = _editStartCenter + GripInteractionMath.ComputeWorldDragDelta(
                        cam,
                        _editStartX,
                        _editStartY,
                        mx,
                        my,
                        _editViewZ);
                    break;
                }
                case GripKind.HeightResize when GetGrip(_activeHandle).Sign > 0:
                {
                    float   proj = Vector3.Dot(
                        GripInteractionMath.ComputeWorldDragDelta(cam, _editStartX, _editStartY, mx, my, _editViewZ),
                        Axis);
                    // Fixed end = bottom at drag start
                    Vector3 fixedEnd  = _editStartCenter - Axis * _editStartHalfHeight;
                    float   newHeight = MathF.Max(_editStartHalfHeight * 2f + proj, 0.02f);
                    HalfHeight = newHeight * 0.5f;
                    Center     = fixedEnd + Axis * HalfHeight;
                    break;
                }
                case GripKind.HeightResize:
                {
                    float   proj = Vector3.Dot(
                        GripInteractionMath.ComputeWorldDragDelta(cam, _editStartX, _editStartY, mx, my, _editViewZ),
                        Axis);
                    // Fixed end = top at drag start
                    Vector3 fixedEnd  = _editStartCenter + Axis * _editStartHalfHeight;
                    float   newHeight = MathF.Max(_editStartHalfHeight * 2f - proj, 0.02f);
                    HalfHeight = newHeight * 0.5f;
                    Center     = fixedEnd - Axis * HalfHeight;
                    break;
                }
                case GripKind.RadiusResize:
                {
                    float proj = GripInteractionMath.ProjectMouseDelta(_editStartX, _editStartY, mx, my, _radialScreenDir);
                    Radius = MathF.Max(_editStartRadius * (1f + proj * MouseDragSensitivity), 0.01f);
                    break;
                }
                default:
                    break;
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

        public override IPointSelectionQuery CreateQuery()
            => new CylinderSelectionQuery(Center, Axis, Radius, HalfHeight);

        private sealed class CylinderSelectionQuery : IPointSelectionQuery
        {
            private readonly float _cx, _cy, _cz;
            private readonly float _ax, _ay, _az;
            private readonly float _r2, _halfHeight;

            public CylinderSelectionQuery(Vector3 center, Vector3 axis, float radius, float halfHeight)
            {
                _cx = center.X; _cy = center.Y; _cz = center.Z;
                _ax = axis.X; _ay = axis.Y; _az = axis.Z;
                _r2 = radius * radius;
                _halfHeight = halfHeight;
            }

            public IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default)
            {
                if (_r2 < 1e-8f || _halfHeight < 1e-4f) return Array.Empty<int>();

                var list = new List<int>(capacity: 256);
                for (int i = 0; i < points.Length; i++)
                {
                    if ((i & 4095) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    float dx = points[i].X - _cx;
                    float dy = points[i].Y - _cy;
                    float dz = points[i].Z - _cz;

                    float h = dx * _ax + dy * _ay + dz * _az;
                    if (MathF.Abs(h) > _halfHeight) continue;

                    float radial2 = dx * dx + dy * dy + dz * dz - h * h;
                    if (radial2 <= _r2) list.Add(i);
                }

                return list;
            }
        }
    }
}
