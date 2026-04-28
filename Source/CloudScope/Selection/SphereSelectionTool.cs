using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space sphere selection with two-phase workflow.
    /// Placement: click center, drag radius. Release → editing mode.
    /// Editing: interactive handle drag (center + 6 poles), G/S keyboard, scroll=fine-tune.
    /// Enter to confirm, Escape to cancel.
    ///
    /// Handle indices:
    ///   0  = center (move)
    ///   1  = +X pole,  2 = -X pole
    ///   3  = +Y pole,  4 = -Y pole
    ///   5  = +Z pole,  6 = -Z pole
    /// </summary>
    public sealed class SphereSelectionTool : ISelectionTool
    {
        public const int HoverNone = -1;
        public const int HandleCount = 7;

        public SelectionToolType ToolType => SelectionToolType.Sphere;
        public bool IsActive { get; private set; }
        public bool IsEditing { get; private set; }
        public bool HasVolume => (IsActive || IsEditing) && Radius > 1e-4f;

        public Vector3 Center { get; set; }
        public float Radius { get; set; }

        // ── Handle state ──────────────────────────────────────────────────────
        public int HoveredHandle { get; set; } = HoverNone;
        public bool IsHandleDragging => _activeHandle >= 0;
        private int _activeHandle = HoverNone;

        // ── G/S/R keyboard edit state ─────────────────────────────────────────
        private EditAction _action = EditAction.None;
        private int _axis = -1;

        // Shared start state for both handle drag and G/S/R edit
        private int _editStartX, _editStartY;
        private Vector3 _editStartCenter;
        private float _editStartRadius;
        private float _editViewZ;  // view-space Z of the handle at drag start (for depth-correct projection)
        // Screen-space outward direction of the active pole handle (unit vector, set in BeginHandleDrag)
        private Vector2 _poleScreenDir;

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

        // ── Handle positions ──────────────────────────────────────────────────

        public Vector3 HandleWorldPosition(int i) => i switch
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

        // ── Handle hit-test ───────────────────────────────────────────────────

        public int HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f)
        {
            if (!IsEditing) return HoverNone;
            int best = HoverNone;
            float bestDist = threshold;
            for (int i = 0; i < HandleCount; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(HandleWorldPosition(i));
                if (behind) continue;
                float d = MathF.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ── Handle drag ───────────────────────────────────────────────────────

        public void BeginHandleDrag(int handle, int mx, int my, OrbitCamera cam)
        {
            if (!IsEditing) return;
            _activeHandle = handle;
            _editStartX = mx; _editStartY = my;
            _editStartCenter = Center;
            _editStartRadius = Radius;

            if (handle == 0)
            {
                // Cache depth of sphere center for depth-correct projection during drag
                _editViewZ = cam.WorldToViewZ(Center);
            }
            else
            {
                // Compute screen-space direction from center toward this pole.
                // Dragging away from center → bigger; toward center → smaller.
                var (cx, cy, _cb) = cam.WorldToScreen(Center);
                var (px, py, _pb) = cam.WorldToScreen(HandleWorldPosition(handle));
                float dx = px - cx, dy = py - cy;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                _poleScreenDir = len > 0.5f ? new Vector2(dx / len, dy / len) : new Vector2(1f, 0f);
            }
        }

        public void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle < 0) return;

            if (_activeHandle == 0)
            {
                // Center: depth-correct world-space drag using cached view-Z
                Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                Vector3 curWorld   = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                Center = _editStartCenter + (curWorld - startWorld);
            }
            else
            {
                // Project mouse delta onto the pole's outward screen direction.
                // Moving away from center = bigger, toward center = smaller.
                float ddx = mx - _editStartX;
                float ddy = my - _editStartY;
                float proj = ddx * _poleScreenDir.X + ddy * _poleScreenDir.Y;
                float factor = 1f + proj * 0.005f;
                Radius = MathF.Max(_editStartRadius * factor, 0.01f);
            }
        }

        public void EndHandleDrag()
        {
            _activeHandle = HoverNone;
        }

        // ── G/S/R keyboard editing ────────────────────────────────────────────

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
                    var (curWorld, _2)  = camera.ScreenToWorldPoint(mx, my, 21);
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
            HoveredHandle = HoverNone;
            _activeHandle = HoverNone;
        }

        public void Cancel()
        {
            IsActive = false;
            IsEditing = false;
            _action = EditAction.None;
            _axis = -1;
            _activeHandle = HoverNone;
            HoveredHandle = HoverNone;
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
