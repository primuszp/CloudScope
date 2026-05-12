using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

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

        public IRenderFrameSession BeginFrame()
        {
            var frame = MetalFrameContext.CurrentFrame;
            if (frame == null || frame.RenderPassDescriptor.NativePtr == IntPtr.Zero)
                return MetalFrameSession.Empty;

            var da = frame.RenderPassDescriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero && da.Texture.NativePtr != IntPtr.Zero)
                MetalFrameContext.SetDepthTexture(da.Texture);

            var cmdBuffer = frame.CommandBuffer;
            if (cmdBuffer.NativePtr == IntPtr.Zero) return MetalFrameSession.Empty;

            var encoder = cmdBuffer.RenderCommandEncoder(frame.RenderPassDescriptor);
            if (encoder.NativePtr == IntPtr.Zero) return MetalFrameSession.Empty;

            MetalFrameContext.SetRenderCommandEncoder(encoder);
            _frameCount++;
            return new MetalFrameSession(frame);
        }

        public void Resize(int width, int height) { }

        private sealed class MetalFrameSession : IRenderFrameSession
        {
            public static readonly MetalFrameSession Empty = new(default);

            private readonly MetalFrameState? _frame;
            private bool _disposed;

            public MetalFrameSession(MetalFrameState? frame)
            {
                _frame = frame;
            }

            public IRenderFrameData FrameData =>
                _frame is null ? EmptyRenderFrameData.Instance : _frame;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;

                var encoder = _frame?.RenderCommandEncoder ?? default;
                if (encoder.NativePtr != IntPtr.Zero)
                    encoder.EndEncoding();

                var cmdBuffer = _frame?.CommandBuffer ?? default;
                if (cmdBuffer.NativePtr != IntPtr.Zero)
                {
                    var drawable = _frame?.Drawable ?? default;
                    if (drawable.NativePtr != IntPtr.Zero)
                        cmdBuffer.PresentDrawable(drawable);
                    cmdBuffer.Commit();
                }

                MetalFrameContext.End();
            }
        }
    }
}
