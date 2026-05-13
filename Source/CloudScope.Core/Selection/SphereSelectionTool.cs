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
        public override IReadOnlyList<GripDescriptor> Grips => GripLayout;

        public override bool HasVolume =>
            (IsActive || IsEditing) && Radius > 1e-4f;

        public float Radius { get; set; }
        private static readonly GripDescriptor[] GripLayout =
        {
            new(0, GripKind.Center),
            new(1, GripKind.RadiusResize, 0,  1),
            new(2, GripKind.RadiusResize, 0, -1),
            new(3, GripKind.RadiusResize, 1,  1),
            new(4, GripKind.RadiusResize, 1, -1),
            new(5, GripKind.RadiusResize, 2,  1),
            new(6, GripKind.RadiusResize, 2, -1),
        };

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

        // ── Handle drag ───────────────────────────────────────────────────────

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartRadius = Radius;
            if (GetGrip(handle).Kind != GripKind.Center)
            {
                _poleScreenDir = GripInteractionMath.ComputeScreenDirection(cam, Center, HandleWorldPosition(handle));
            }
        }

        public override void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle < 0) return;

            if (GetGrip(_activeHandle).Kind == GripKind.Center)
            {
                // Depth-correct world-space drag
                Center = _editStartCenter + GripInteractionMath.ComputeWorldDragDelta(
                    cam,
                    _editStartX,
                    _editStartY,
                    mx,
                    my,
                    _editViewZ);
            }
            else
            {
                // Project mouse delta onto pole's outward screen direction
                float proj   = GripInteractionMath.ProjectMouseDelta(_editStartX, _editStartY, mx, my, _poleScreenDir);
                float factor = 1f + proj * MouseDragSensitivity;
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
