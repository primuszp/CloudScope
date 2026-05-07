using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class MetalRenderBackend : IRenderBackend
    {
        public RenderBackendKind Kind => RenderBackendKind.Metal;

        public IPointCloudRenderer  CreatePointCloudRenderer()  => new MetalPointCloudRenderer();
        public IHighlightRenderer   CreateHighlightRenderer()   => new MetalHighlightRenderer();
        public IOverlayRenderer     CreateOverlayRenderer()     => new MetalOverlayRenderer();
        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => MetalRendererFactory.CreateSelectionGizmoRenderers();
        public IDepthPicker CreateDepthPicker() => new MetalDepthPicker();

        public void InitializeFrameState() { }

        public void BeginFrame()
        {
            var descriptor = MetalFrameContext.CurrentRenderPassDescriptor;
            if (descriptor.NativePtr == IntPtr.Zero) return;

            var ca = descriptor.ColorAttachments.Object(0);
            if (ca.NativePtr != IntPtr.Zero)
            {
                ca.LoadAction  = MTLLoadAction.Clear;
                ca.StoreAction = MTLStoreAction.Store;
            }

            var da = descriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero)
            {
                if (da.Texture.NativePtr != IntPtr.Zero)
                    MetalFrameContext.SetDepthTexture(da.Texture);

                da.LoadAction  = MTLLoadAction.Clear;
                da.StoreAction = MTLStoreAction.Store;
                da.ClearDepth  = 1.0;
            }

            var cmdBuffer = MetalFrameContext.CurrentCommandBuffer;
            if (cmdBuffer.NativePtr != IntPtr.Zero)
            {
                MetalFrameContext.CurrentRenderCommandEncoder = cmdBuffer.RenderCommandEncoder(descriptor);
            }
        }

        public void Resize(int width, int height) { }
    }
}
