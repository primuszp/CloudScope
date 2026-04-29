using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space sphere selection with two-phase workflow.
    /// Placement: click center, drag radius. Release → Editing.
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

        public override bool HasVolume =>
            (IsActive || IsEditing) && Radius > 1e-4f;

        public float Radius { get; set; }

        // ── Sphere-specific drag state ────────────────────────────────────────
        private float   _editStartRadius;
        private Vector2 _poleScreenDir;

        // ── Placement ─────────────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Center   = worldPt;
            Radius   = 0f;
            Phase    = ToolPhase.Drawing;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Radius = (worldPt - Center).Length;
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (!IsActive) return;
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
            if (handle != 0)
            {
                // Screen-space direction from center toward this pole
                var (cx, cy, _cb) = cam.WorldToScreen(Center);
                var (px, py, _pb) = cam.WorldToScreen(HandleWorldPosition(handle));
                float dx = px - cx, dy = py - cy;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                _poleScreenDir = len > 0.5f ? new Vector2(dx / len, dy / len) : new Vector2(1f, 0f);
            }
        }

        public override void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle < 0) return;

            if (_activeHandle == 0)
            {
                // Depth-correct world-space drag
                Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                Vector3 curWorld   = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                Center = _editStartCenter + (curWorld - startWorld);
            }
            else
            {
                // Project mouse delta onto pole's outward screen direction
                float proj   = (mx - _editStartX) * _poleScreenDir.X + (my - _editStartY) * _poleScreenDir.Y;
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

        public override HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            if (Radius < 1e-4f) return new HashSet<int>();

            float r2 = Radius * Radius;
            float cx = Center.X, cy = Center.Y, cz = Center.Z;

            var list = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                float dx = points[i].X - cx;
                float dy = points[i].Y - cy;
                if (dx * dx + dy * dy > r2) continue;
                float dz = points[i].Z - cz;
                if (dx * dx + dy * dy + dz * dz <= r2) list.Add(i);
            }
            return new HashSet<int>(list);
        }
    }
}
