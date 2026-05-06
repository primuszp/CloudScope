using System;
using CloudScope.Rendering;

namespace CloudScope.Platform.Metal
{
    public sealed class MetalRenderBackend : IRenderBackend
    {
#if MACOS
        private readonly Metal.IMTLCommandQueue? commandQueue;
#endif

        public MetalRenderBackend()
        {
#if MACOS
            var device = Metal.MTLDevice.SystemDefault;
            commandQueue = device?.CreateCommandQueue();
#endif
        }

        public RenderBackendKind Kind => RenderBackendKind.Metal;

        public IPointCloudRenderer CreatePointCloudRenderer() => MetalRendererFactory.CreatePointCloudRenderer();

        public IHighlightRenderer CreateHighlightRenderer() => MetalRendererFactory.CreateHighlightRenderer();

        public IOverlayRenderer CreateOverlayRenderer() => MetalRendererFactory.CreateOverlayRenderer();

        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => MetalRendererFactory.CreateSelectionGizmoRenderers();

        public IDepthPicker CreateDepthPicker() => MetalRendererFactory.CreateDepthPicker();

        public void InitializeFrameState()
        {
#if !MACOS
            throw NotImplemented();
#endif
        }

        public void BeginFrame()
        {
#if MACOS
            var view = MetalFrameContext.CurrentView;
            var descriptor = view?.CurrentRenderPassDescriptor;

            if (descriptor == null)
                return;

            descriptor.ColorAttachments[0].LoadAction = Metal.MTLLoadAction.Clear;
            descriptor.ColorAttachments[0].StoreAction = Metal.MTLStoreAction.Store;
            if (descriptor.DepthAttachment != null)
            {
                descriptor.DepthAttachment.LoadAction = Metal.MTLLoadAction.Clear;
                descriptor.DepthAttachment.StoreAction = Metal.MTLStoreAction.DontCare;
                descriptor.DepthAttachment.ClearDepth = 1.0;
            }
#else
            throw NotImplemented();
#endif
        }

        public void Resize(int width, int height)
        {
        }

#if MACOS
        internal Metal.IMTLCommandBuffer? CreateCommandBuffer() => commandQueue?.CommandBuffer();
#endif

        private static NotSupportedException NotImplemented() =>
            new("Metal backend requires a macOS target. Use CLOUDSCOPE_RENDER_BACKEND=opengl on this build.");
    }
}
