using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// Two-phase selection tool interface.
    /// Phase 1 (Drawing): OnMouseDown → OnMouseMove → OnMouseUp → enters Editing.
    /// Phase 2 (Editing): handle drag, G/S/R keyboard, scroll, Enter to confirm.
    /// </summary>
    public interface ISelectionTool : IGripTarget
    {
        SelectionToolType ToolType { get; }

        ToolPhase Phase     { get; }
        bool      IsActive  { get; }   // Phase == Drawing
        bool      IsEditing { get; }   // Phase == Editing
        bool      HasVolume { get; }

        Vector3 Center { get; set; }

        // ── View-constrained grips ────────────────────────────────────────────
        /// <summary>Active per-viewport grip view constraint (set before render/hit-test).</summary>
        GripViewConstraint ViewConstraint { get; set; }

        /// <summary>Orientation of the volume; SCALE and ROTATE need it, gestures set it too.</summary>
        Quaternion Rotation { get; set; }
        /// <summary>True when grip <paramref name="index"/> is shown/interactive under the current constraint.</summary>
        /// <summary>Handle index of the center grip used for in-plane body translation.</summary>
        /// <summary>True when the screen point lies within the volume's body (for click-drag translation).</summary>

        // ── Handle interaction ────────────────────────────────────────────────

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

        /// <summary>Multiplies the tool's dimensions, for a typed SCALE factor.</summary>
        void ScaleBy(float factor);
        void SetAxisConstraint(int axis);

        void Confirm();
        void Cancel();

        IPointSelectionQuery CreateQuery();

        // ── Renderer helpers ──────────────────────────────────────────────────
        EditAction CurrentAction { get; }
        int        AxisConstraint { get; }
    }
}
