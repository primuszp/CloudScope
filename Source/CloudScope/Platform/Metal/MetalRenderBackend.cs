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

            // The MTKView already has clear color, load/store actions set up properly
            // based on the view's ClearColor and DepthStencilPixelFormat properties.
            // We just need to stash the depth texture and create the single encoder.

            var da = descriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero && da.Texture.NativePtr != IntPtr.Zero)
            {
                MetalFrameContext.SetDepthTexture(da.Texture);
            }

            var cmdBuffer = MetalFrameContext.CurrentCommandBuffer;
            if (cmdBuffer.NativePtr != IntPtr.Zero)
            {
                var encoder = cmdBuffer.RenderCommandEncoder(descriptor);
                MetalFrameContext.CurrentRenderCommandEncoder = encoder;
            }
        }

        public void Resize(int width, int height) { }
    }
}
