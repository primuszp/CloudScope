using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space sphere selection with two-phase workflow.
    /// Placement: click center, drag radius. Release → editing mode.
    /// Editing: G=grab center, S=scale radius, scroll=fine-tune radius.
    /// Enter to confirm, Escape to cancel.
    /// </summary>
    public sealed class SphereSelectionTool : ISelectionTool
    {
        public SelectionToolType ToolType => SelectionToolType.Sphere;
        public bool IsActive { get; private set; }
        public bool IsEditing { get; private set; }
        public bool HasVolume => (IsActive || IsEditing) && Radius > 1e-4f;

        public Vector3 Center { get; set; }
        public float Radius { get; set; }

        // ── Edit state ────────────────────────────────────────────────────────
        private EditAction _action = EditAction.None;
        private int _axis = -1;
        private int _editStartX, _editStartY;
        private Vector3 _editStartCenter;
        private float _editStartRadius;

        // ── Placement ─────────────────────────────────────────────────────────

        public void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Center = worldPt;
            Radius = 0f;
            IsActive = true;
            IsEditing = false;
        }

        public void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Radius = (worldPt - Center).Length;
        }

        public void OnMouseUp(int mx, int my)
        {
            if (!IsActive) return;
            IsActive = false;
            if (Radius > 0.01f)
                IsEditing = true;
        }

        // ── Editing ───────────────────────────────────────────────────────────

        public void BeginGrab(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _action = EditAction.Grab;
            _editStartX = mx; _editStartY = my;
            _editStartCenter = Center;
        }

        public void BeginScale(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _action = EditAction.Scale;
            _editStartX = mx; _editStartY = my;
            _editStartRadius = Radius;
        }

        public void BeginRotate(int mx, int my, OrbitCamera camera)
        {
            // No-op for sphere (symmetric)
        }

        public void UpdateEdit(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;

            switch (_action)
            {
                case EditAction.Grab:
                {
                    var (startWorld, _) = camera.ScreenToWorldPoint(_editStartX, _editStartY, 21);
                    var (curWorld, _2) = camera.ScreenToWorldPoint(mx, my, 21);
                    Vector3 delta = curWorld - startWorld;
                    if (_axis >= 0)
                    {
                        Vector3 mask = Vector3.Zero;
                        if (_axis == 0) mask.X = 1f;
                        else if (_axis == 1) mask.Y = 1f;
                        else mask.Z = 1f;
                        delta *= mask;
                    }
                    Center = _editStartCenter + delta;
                    break;
                }
                case EditAction.Scale:
                {
                    int dx = mx - _editStartX;
                    float factor = 1f + dx * 0.005f;
                    Radius = MathF.Max(_editStartRadius * factor, 0.01f);
                    break;
                }
            }
        }

        public void EndEdit()
        {
            _action = EditAction.None;
            _axis = -1;
        }

        public void AdjustScale(float delta)
        {
            if (!IsEditing) return;
            float factor = 1f + delta * 0.08f;
            Radius = MathF.Max(Radius * factor, 0.01f);
        }

        public void SetAxisConstraint(int axis) => _axis = axis;

        public void Confirm()
        {
            IsEditing = false;
            IsActive = false;
        }

        public void Cancel()
        {
            IsActive = false;
            IsEditing = false;
            _action = EditAction.None;
            _axis = -1;
            Radius = 0f;
        }

        // ── Resolution ────────────────────────────────────────────────────────

        public HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
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

        // ── Helpers for renderer ──────────────────────────────────────────────
        public EditAction CurrentAction => _action;
        public int AxisConstraint => _axis;
    }
}
