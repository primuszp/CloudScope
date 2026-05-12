using System;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    public interface ISelectionGizmoRenderer : IDisposable
    {
        void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera);
    }

    public interface IBoxSelectionGizmoRenderer : ISelectionGizmoRenderer
    {
        void RenderPlacementRect(IRenderFrameData frameData, int x0, int y0, int x1, int y1, int viewportWidth, int viewportHeight);
    }
}
