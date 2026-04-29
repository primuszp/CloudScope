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

        // ── Handle interaction ────────────────────────────────────────────────
        int  HoveredHandle    { get; set; }
        bool IsHandleDragging { get; }
        int  HitTestHandles(int mx, int my, OrbitCamera cam, float threshold = 12f);
        void BeginHandleDrag(int handle, int mx, int my, OrbitCamera cam);
        void UpdateHandleDrag(int mx, int my, OrbitCamera cam);
        void EndHandleDrag();

        // ── Phase 1: Placement ────────────────────────────────────────────────
        void OnMouseDown(int mx, int my, OrbitCamera camera);
        void OnMouseMove(int mx, int my, OrbitCamera camera);
        void OnMouseUp(int mx, int my);

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

        HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH);

        // ── Renderer helpers ──────────────────────────────────────────────────
        EditAction CurrentAction { get; }
        int        AxisConstraint { get; }
    }
}
