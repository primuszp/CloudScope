using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public enum ToolPhase { Idle, Drawing, Editing }

    /// <summary>
    /// Shared state and logic for all selection tools.
    /// Concrete tools override abstract members and virtual hooks.
    /// </summary>
    public abstract class SelectionToolBase : ISelectionTool
    {
        public const int   HoverNone           = -1;
        protected const float ScrollScaleFactor   = 0.08f;
        protected const float MouseDragSensitivity = 0.005f;

        // ── Phase ─────────────────────────────────────────────────────────────
        public ToolPhase Phase { get; protected set; } = ToolPhase.Idle;
        public bool IsActive  => Phase == ToolPhase.Drawing;
        public bool IsEditing => Phase == ToolPhase.Editing;

        // ── Center ────────────────────────────────────────────────────────────
        public virtual Vector3 Center { get; set; }

        // ── Handle state ──────────────────────────────────────────────────────
        public int  HoveredHandle    { get; set; } = HoverNone;
        public bool IsHandleDragging => _activeHandle >= 0;
        protected int _activeHandle  = HoverNone;

        // ── Shared edit state ─────────────────────────────────────────────────
        protected int        _editStartX, _editStartY;
        protected Vector3    _editStartCenter;
        protected float      _editViewZ;
        protected EditAction _kbAction = EditAction.None;
        protected int        _kbAxis   = -1;

        // ── Abstract ──────────────────────────────────────────────────────────
        public abstract SelectionToolType ToolType  { get; }
        public abstract bool   HasVolume            { get; }
        public abstract int    HandleCount          { get; }
        public abstract Vector3 HandleWorldPosition(int i);
        public abstract void   OnMouseDown(int mx, int my, OrbitCamera camera);
        public abstract void   OnMouseMove(int mx, int my, OrbitCamera camera);
        public abstract void   OnMouseUp(int mx, int my);
        public abstract void   UpdateHandleDrag(int mx, int my, OrbitCamera cam);
        public abstract void   AdjustScale(float delta);
        public abstract HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH);

        // ── Handle interaction ────────────────────────────────────────────────

        public virtual int HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f)
        {
            if (Phase != ToolPhase.Editing) return HoverNone;
            int   best     = HoverNone;
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

        public virtual void BeginHandleDrag(int handle, int mx, int my, OrbitCamera cam)
        {
            _activeHandle    = handle;
            _editStartX      = mx;
            _editStartY      = my;
            _editStartCenter = Center;
            _editViewZ       = cam.WorldToViewZ(HandleWorldPosition(handle));
            OnBeginHandleDragExtra(handle, mx, my, cam);
        }
        protected virtual void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam) { }

        public void EndHandleDrag() => _activeHandle = HoverNone;

        // ── Keyboard edit ─────────────────────────────────────────────────────

        public virtual void BeginGrab(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction        = EditAction.Grab;
            _editStartX      = mx;
            _editStartY      = my;
            _editStartCenter = Center;
            _editViewZ       = camera.WorldToViewZ(Center);
        }

        public virtual void BeginScale(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction   = EditAction.Scale;
            _editStartX = mx;
            OnBeginScaleExtra(mx, my, camera);
        }
        protected virtual void OnBeginScaleExtra(int mx, int my, OrbitCamera cam) { }

        public virtual void BeginRotate(int mx, int my, OrbitCamera camera) { }

        public virtual void UpdateEdit(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            if (_kbAction == EditAction.Grab)
            {
                Vector3 s = camera.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                Vector3 c = camera.ScreenToWorldAtDepth(mx, my, _editViewZ);
                Vector3 d = c - s;
                if (_kbAxis >= 0)
                {
                    Vector3 m = _kbAxis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                    d = m * Vector3.Dot(d, m);
                }
                Center = _editStartCenter + d;
            }
            else
            {
                UpdateEditShape(_kbAction, mx, my, camera);
            }
        }
        protected virtual void UpdateEditShape(EditAction action, int mx, int my, OrbitCamera cam) { }

        public void EndEdit()             { _kbAction = EditAction.None; _kbAxis = -1; }
        public void SetAxisConstraint(int axis) => _kbAxis = axis;

        public virtual void Confirm()
        {
            Phase         = ToolPhase.Idle;
            HoveredHandle = HoverNone;
            _activeHandle = HoverNone;
        }

        public virtual void Cancel()
        {
            Phase         = ToolPhase.Idle;
            _kbAction     = EditAction.None;
            _kbAxis       = -1;
            _activeHandle = HoverNone;
            HoveredHandle = HoverNone;
        }

        // ── Renderer helpers ──────────────────────────────────────────────────
        public EditAction CurrentAction => _kbAction;
        public int        AxisConstraint => _kbAxis;
    }
}
