using System;
using CloudScope.Rendering;

namespace CloudScope.Platform.Metal
{
    public sealed class MetalRenderBackend : IRenderBackend
    {
        public RenderBackendKind Kind => RenderBackendKind.Metal;

        public IPointCloudRenderer CreatePointCloudRenderer() => throw NotImplemented();

        public IHighlightRenderer CreateHighlightRenderer() => throw NotImplemented();

        public IOverlayRenderer CreateOverlayRenderer() => throw NotImplemented();

        public SelectionGizmoRenderers CreateSelectionGizmoRenderers() => throw NotImplemented();

        public IDepthPicker CreateDepthPicker() => throw NotImplemented();

        public void InitializeFrameState() => throw NotImplemented();

        public void BeginFrame() => throw NotImplemented();

        public void Resize(int width, int height) => throw NotImplemented();

        private static NotSupportedException NotImplemented() =>
            new("Metal backend is not implemented yet. Use CLOUDSCOPE_RENDER_BACKEND=opengl.");
    }
}
