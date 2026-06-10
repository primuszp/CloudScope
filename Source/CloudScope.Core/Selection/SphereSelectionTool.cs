using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space sphere selection with two-phase workflow.
    /// Placement: click screen center, drag screen radius projected at center depth.
    /// Release → Editing.
    /// Editing: handle drag (center + 6 poles), G/S keyboard, scroll = fine-tune.
    /// Enter to confirm, Escape to cancel.
    ///
    /// Handle indices:
    ///   0 = center (move)
    ///   1 = +X pole,  2 = -X pole
    ///   3 = +Y pole,  4 = -Y pole
    ///   5 = +Z pole,  6 = -Z pole
    /// </summary>
    public sealed class SphereSelectionTool : SelectionToolBase
    {
        public override SelectionToolType ToolType  => SelectionToolType.Sphere;
        public override int               HandleCount => 7;
        public override IReadOnlyList<GripDescriptor> Grips => BuildGrips();

        public override bool HasVolume =>
            (IsActive || IsEditing) && Radius > 1e-4f;

        public float Radius { get; set; }
        private readonly GripDescriptor[] _grips = new GripDescriptor[7];

        private IReadOnlyList<GripDescriptor> BuildGrips()
        {
            _grips[0] = GripDescriptor.Center(0, Center);
            for (int i = 1; i < 7; i++)
            {
                int axis = (i - 1) / 2;
                int sign = i % 2 == 1 ? 1 : -1;
                Vector3 direction = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                _grips[i] = GripDescriptor.Uniform(i, GripKind.RadiusResize, HandleWorldPosition(i), direction * sign, axis, sign);
            }
            return _grips;
        }

        // ── Sphere-specific drag state ────────────────────────────────────────
        private float   _editStartRadius;
        private Vector2 _poleScreenDir;

        // ── Placement ─────────────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Center = worldPt;
            Radius = 0f;
            Phase  = ToolPhase.Drawing;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            Radius = camera.ScreenToWorldRadius(Center, mx, my);
        }

        public override void OnMouseUp(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            Radius = camera.ScreenToWorldRadius(Center, mx, my);
            Phase = Radius > 0.01f ? ToolPhase.Editing : ToolPhase.Idle;
        }

        // ── Handle positions ──────────────────────────────────────────────────

        public override Vector3 HandleWorldPosition(int i) => i switch
        {
            0 => Center,
            1 => Center + new Vector3( Radius, 0f, 0f),
            2 => Center + new Vector3(-Radius, 0f, 0f),
            3 => Center + new Vector3(0f,  Radius, 0f),
            4 => Center + new Vector3(0f, -Radius, 0f),
            5 => Center + new Vector3(0f, 0f,  Radius),
            6 => Center + new Vector3(0f, 0f, -Radius),
            _ => Center
        };

        // ── Handle hit test ───────────────────────────────────────────────────

        protected override float GetGripHitDistance(GripDescriptor grip, int mx, int my, OrbitCamera cam)
        {
            if (grip.Kind == GripKind.RadiusResize)
            {
                float len = cam.WorldUnitsPerPixel(Center) * GripArrowSupport.ArrowHeightPixels;
                return GripArrowSupport.ScreenHitDistance(
                    GripArrowSupport.Create(grip, len), cam, mx, my);
            }
            return base.GetGripHitDistance(grip, mx, my, cam);
        }

        // ── Handle drag ───────────────────────────────────────────────────────

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartRadius = Radius;
            if (ActiveGrip.Kind != GripKind.Center)
            {
                _poleScreenDir = GripInteractionMath.ComputeScreenDirection(cam, Center, HandleWorldPosition(handle));
            }
        }

        public override void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle < 0) return;

            if (ActiveGrip.Kind == GripKind.Center)
            {
                // Depth-correct world-space drag
                Center = _editStartCenter + GripManipulator3D.Translation(ActiveDragContext, cam, mx, my);
            }
            else
            {
                // Project mouse delta onto pole's outward screen direction
                float factor = GripManipulator3D.ScreenScale(ActiveDragContext, cam, mx, my, MouseDragSensitivity);
                Radius = MathF.Max(_editStartRadius * factor, 0.01f);
            }
        }

        // ── Keyboard scale ────────────────────────────────────────────────────

        protected override void OnBeginScaleExtra(int mx, int my, OrbitCamera cam)
            => _editStartRadius = Radius;

        protected override void UpdateEditShape(EditAction action, int mx, int my, OrbitCamera cam)
        {
            if (action == EditAction.Scale)
            {
                float factor = 1f + (mx - _editStartX) * MouseDragSensitivity;
                Radius = MathF.Max(_editStartRadius * factor, 0.01f);
            }
        }

        public override void AdjustScale(float delta)
        {
            if (!IsEditing) return;
            Radius = MathF.Max(Radius * (1f + delta * ScrollScaleFactor), 0.01f);
        }

        public override void Cancel()
        {
            base.Cancel();
            Radius = 0f;
        }

        // ── Resolution ────────────────────────────────────────────────────────

        public override IPointSelectionQuery CreateQuery()
            => new SphereSelectionQuery(Center, Radius);

        private sealed class SphereSelectionQuery : IPointSelectionQuery
        {
            private readonly float _cx, _cy, _cz, _r2;

            public SphereSelectionQuery(Vector3 center, float radius)
            {
                _cx = center.X;
                _cy = center.Y;
                _cz = center.Z;
                _r2 = radius * radius;
            }

            public IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default)
            {
                if (_r2 < 1e-8f) return Array.Empty<int>();

                var list = new List<int>();
                for (int i = 0; i < points.Length; i++)
                {
                    if ((i & 4095) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    float dx = points[i].X - _cx;
                    float dy = points[i].Y - _cy;
                    float dxy = dx * dx + dy * dy;
                    if (dxy > _r2) continue;
                    float dz = points[i].Z - _cz;
                    if (dxy + dz * dz <= _r2) list.Add(i);
                }

                return list;
            }
        }
    }
}
