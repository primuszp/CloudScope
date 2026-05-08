using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class MetalRenderBackend : IRenderBackend
    {
        public RenderBackendKind Kind => RenderBackendKind.Metal;

        // ── Diagnostic triangle ─────────────────────────────────────────────────────
        // Draws a solid red triangle via the same encoder CloudScope uses.
        // If the triangle appears → encoder works, bug is in the renderers.
        // If NOT → encoder itself broken (descriptor/format issue).
        private MTLRenderPipelineState _diagPipeline;
        private int _frameCount;

        public IPointCloudRenderer  CreatePointCloudRenderer()  => new MetalPointCloudRenderer();
        public IHighlightRenderer   CreateHighlightRenderer()   => new MetalHighlightRenderer();
        public IOverlayRenderer     CreateOverlayRenderer()     => new MetalOverlayRenderer();
        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => MetalRendererFactory.CreateSelectionGizmoRenderers();
        public IDepthPicker CreateDepthPicker() => new NullDepthPicker();

        public void InitializeFrameState()
        {
            // Diagnostics removed.
        }

        public void BeginFrame()
        {
            var descriptor = MetalFrameContext.CurrentRenderPassDescriptor;
            if (descriptor.NativePtr == IntPtr.Zero) return;

            var da = descriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero && da.Texture.NativePtr != IntPtr.Zero)
                MetalFrameContext.SetDepthTexture(da.Texture);

            var cmdBuffer = MetalFrameContext.CurrentCommandBuffer;
            if (cmdBuffer.NativePtr == IntPtr.Zero) return;

            var encoder = cmdBuffer.RenderCommandEncoder(descriptor);
            if (encoder.NativePtr == IntPtr.Zero) return;

            MetalFrameContext.CurrentRenderCommandEncoder = encoder;
            _frameCount++;
        }

        public void Resize(int width, int height) { }
    }
}
