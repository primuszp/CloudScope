using System;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Rendering
{
    public sealed class SelectionGizmoRenderers
    {
        public SelectionGizmoRenderers(
            IBoxSelectionGizmoRenderer box,
            ISelectionGizmoRenderer sphere,
            ISelectionGizmoRenderer cylinder)
        {
            Box = box;
            Sphere = sphere;
            Cylinder = cylinder;
        }

        public IBoxSelectionGizmoRenderer Box { get; }
        public ISelectionGizmoRenderer Sphere { get; }
        public ISelectionGizmoRenderer Cylinder { get; }
        public ISelectionGizmoRenderer Resolve(SelectionToolType toolType) => toolType switch
        {
            SelectionToolType.Box => Box,
            SelectionToolType.Sphere => Sphere,
            SelectionToolType.Cylinder => Cylinder,
            _ => throw new ArgumentOutOfRangeException(nameof(toolType), toolType, "Unsupported selection tool type.")
        };

        public void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
            => Resolve(tool.ToolType).Render(frameData, tool, view, proj, camera);

        public bool TryRenderPlacement(
            IRenderFrameData frameData,
            ISelectionTool tool,
            int viewportWidth,
            int viewportHeight)
        {
            if (tool is not BoxSelectionTool box || box.Phase != ToolPhase.Drawing)
                return false;

            Box.RenderPlacementRect(frameData, box.StartX, box.StartY, box.EndX, box.EndY, viewportWidth, viewportHeight);
            return true;
        }
    }
}
