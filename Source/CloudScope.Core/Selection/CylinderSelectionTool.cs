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
    ///   Click sets center from screen input, drag sets radius by projecting the
    ///   screen cursor at the center depth. Release → thin disk placed, Phase = Editing.
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
        public override IReadOnlyList<GripDescriptor> Grips => BuildGrips();

        public override bool HasVolume =>
            IsEditing && Radius > 1e-4f && HalfHeight > 1e-4f;

        public float      Radius     { get; set; }
        public float      HalfHeight { get; set; }
        public override   Quaternion Rotation { get; set; } = DefaultRotation;
        private readonly GripDescriptor[] _grips = new GripDescriptor[10];

        private IReadOnlyList<GripDescriptor> BuildGrips()
        {
            _grips[0] = GripDescriptor.Center(0, Center);
            _grips[1] = GripDescriptor.AlongAxis(1, GripKind.HeightResize, HandleWorldPosition(1), Axis, 2, 1, true);
            _grips[2] = GripDescriptor.AlongAxis(2, GripKind.HeightResize, HandleWorldPosition(2), -Axis, 2, -1);
            _grips[3] = GripDescriptor.Uniform(3, GripKind.RadiusResize, HandleWorldPosition(3), LocalX, 0, 1);
            _grips[4] = GripDescriptor.Uniform(4, GripKind.RadiusResize, HandleWorldPosition(4), -LocalX, 0, -1);
            _grips[5] = GripDescriptor.Uniform(5, GripKind.RadiusResize, HandleWorldPosition(5), LocalY, 1, 1);
            _grips[6] = GripDescriptor.Uniform(6, GripKind.RadiusResize, HandleWorldPosition(6), -LocalY, 1, -1);
            _grips[7] = GripDescriptor.Ring(7, Center, LocalX, 0);
            _grips[8] = GripDescriptor.Ring(8, Center, LocalY, 1);
            _grips[9] = GripDescriptor.Ring(9, Center, Axis, 2);
            return _grips;
        }

        // Rotate 90° around X: local Y → world Z (upright, Z is up in LiDAR coordinates)
        private static readonly Quaternion DefaultRotation = Quaternion.Identity;

        // Local axes derived from rotation
        public  Vector3 Axis   => Vector3.Transform(Vector3.UnitZ, Rotation);
        private Vector3 LocalX => Vector3.Transform(Vector3.UnitX, Rotation);
        private Vector3 LocalY => Vector3.Transform(Vector3.UnitY, Rotation);

        // Ring radius: slightly larger than the cylinder for visual clarity
        public float RingRadius => MathF.Max(Radius, HalfHeight) * 1.35f;

        public static bool IsRingHandle(int i) => i is >= 7 and <= 9;
        public static int  RingAxis(int i)     => i - 7;

        // ── Drag/edit state ───────────────────────────────────────────────────
        private float      _editStartRadius;
        private float      _editStartHalfHeight;
        private Vector2    _radialScreenDir;
        private float      _placementViewZ;

        // ── Phase 1: Placement ────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 11);
            _placementViewZ = camera.WorldToViewZ(worldPt);
            Center          = worldPt;
            Radius          = 0f;
            HalfHeight      = 0f;
            Rotation        = DefaultRotation;
            Phase           = ToolPhase.Drawing;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            Radius = ComputePlacementRadius(mx, my, camera);
        }

        public override void OnMouseUp(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            Radius = ComputePlacementRadius(mx, my, camera);
            if (Radius < 0.01f) { Phase = ToolPhase.Idle; return; }
            HalfHeight = Radius;   // default: cube-ish proportions, user extrudes from here
            Phase      = ToolPhase.Editing;
        }

        private float ComputePlacementRadius(int mx, int my, OrbitCamera camera)
        {
            Vector3 worldPt = camera.ScreenToWorldAtDepth(mx, my, _placementViewZ);
            Vector3 radial = worldPt - Center;
            radial -= Vector3.Dot(radial, Axis) * Axis;
            return MathF.Max(radial.Length, 0f);
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
            if (grip.Kind == GripKind.HeightResize || grip.Kind == GripKind.RadiusResize)
            {
                float px  = grip.IsPrimary ? GripArrowSupport.ArrowHeightPixels * 1.2f : GripArrowSupport.ArrowHeightPixels;
                float len = cam.WorldUnitsPerPixel(grip.Position) * px;
                return GripArrowSupport.ScreenHitDistance(
                    GripArrowSupport.Create(grip, len), cam, mx, my);
            }

            return base.GetGripHitDistance(grip, mx, my, cam);
        }

        public float ArrowLength(GripDescriptor grip) =>
            MathF.Max(MathF.Max(Radius, HalfHeight) * (grip.IsPrimary ? 0.55f : 0.30f), 0.05f);

        // ── Handle drag ───────────────────────────────────────────────────────

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;
            _editStartRotation   = Rotation;

            if (ActiveGrip.Kind == GripKind.RadiusResize)
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

            GripDescriptor grip = ActiveGrip;
            switch (grip.Kind)
            {
                case GripKind.Center:
                {
                    Center = _editStartCenter + GripManipulator3D.Translation(ActiveDragContext, cam, mx, my);
                    break;
                }
                case GripKind.HeightResize when grip.Sign > 0:
                {
                    float proj = GripManipulator3D.AxisDistance(ActiveDragContext, cam, mx, my);
                    // Fixed end = bottom at drag start
                    Vector3 fixedEnd  = _editStartCenter - Axis * _editStartHalfHeight;
                    float   newHeight = MathF.Max(_editStartHalfHeight * 2f + proj, 0.02f);
                    HalfHeight = newHeight * 0.5f;
                    Center     = fixedEnd + Axis * HalfHeight;
                    break;
                }
                case GripKind.HeightResize:
                {
                    float proj = -GripManipulator3D.AxisDistance(ActiveDragContext, cam, mx, my);
                    // Fixed end = top at drag start
                    Vector3 fixedEnd  = _editStartCenter + Axis * _editStartHalfHeight;
                    float   newHeight = MathF.Max(_editStartHalfHeight * 2f - proj, 0.02f);
                    HalfHeight = newHeight * 0.5f;
                    Center     = fixedEnd - Axis * HalfHeight;
                    break;
                }
                case GripKind.RadiusResize:
                {
                    float factor = GripManipulator3D.ScreenScale(ActiveDragContext, cam, mx, my, MouseDragSensitivity);
                    Radius = MathF.Max(_editStartRadius * factor, 0.01f);
                    break;
                }
                default:
                    break;
            }
        }

        // ── Keyboard editing ──────────────────────────────────────────────────

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
