using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// Two-phase selection tool interface.
    /// Phase 1 (Drawing): OnMouseDown → OnMouseMove → OnMouseUp → enters Editing.
    /// Phase 2 (Editing): handle drag, G/S/R keyboard, scroll, Enter to confirm.
    /// </summary>
    public interface ISelectionTool
    {
        SelectionToolType ToolType { get; }

        ToolPhase Phase     { get; }
        bool      IsActive  { get; }   // Phase == Drawing
        bool      IsEditing { get; }   // Phase == Editing
        bool      HasVolume { get; }

        Vector3 Center { get; }
        IReadOnlyList<GripDescriptor> Grips { get; }

        // ── View-constrained grips ────────────────────────────────────────────
        /// <summary>Active per-viewport grip view constraint (set before render/hit-test).</summary>
        GripViewConstraint ViewConstraint { get; set; }
        /// <summary>True when grip <paramref name="index"/> is shown/interactive under the current constraint.</summary>
        bool IsGripVisible(int index);
        /// <summary>Handle index of the center grip used for in-plane body translation.</summary>
        int CenterGripIndex { get; }
        /// <summary>True when the screen point lies within the volume's body (for click-drag translation).</summary>
        bool HitTestBody(int mx, int my, OrbitCamera cam);

        // ── Handle interaction ────────────────────────────────────────────────
        int  HoveredHandle    { get; set; }
        int  ActiveHandle     { get; }
        bool IsHandleDragging { get; }
        int  HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f);
        GripDescriptor GetGrip(int handle);
        bool TryGetGrip(int handle, out GripDescriptor grip);
        void BeginHandleDrag(int handle, int mx, int my, OrbitCamera cam);
        void UpdateHandleDrag(int mx, int my, OrbitCamera cam);
        void EndHandleDrag();

        // ── Phase 1: Placement ────────────────────────────────────────────────
        void OnMouseDown(int mx, int my, OrbitCamera camera);
        void OnMouseMove(int mx, int my, OrbitCamera camera);
        void OnMouseUp(int mx, int my, OrbitCamera camera);

        // ── Phase 2: Keyboard editing ─────────────────────────────────────────
        void BeginGrab(int mx, int my, OrbitCamera camera);
        void BeginScale(int mx, int my, OrbitCamera camera);
        void BeginRotate(int mx, int my, OrbitCamera camera);
        void UpdateEdit(int mx, int my, OrbitCamera camera);
        void EndEdit();

        void AdjustScale(float delta);
        void SetAxisConstraint(int axis);

        void Confirm();
        void Cancel();

        IPointSelectionQuery CreateQuery();

        // ── Renderer helpers ──────────────────────────────────────────────────
        EditAction CurrentAction { get; }
        int        AxisConstraint { get; }
    }
}
