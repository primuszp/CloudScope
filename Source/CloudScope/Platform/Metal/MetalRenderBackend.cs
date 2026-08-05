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

        public IPointCloudRenderer  CreatePointCloudRenderer()  => new MetalPointCloudRenderer();
        public IHighlightRenderer   CreateHighlightRenderer()   => new MetalHighlightRenderer();
        public IOverlayRenderer     CreateOverlayRenderer()     => new MetalOverlayRenderer();
        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => MetalRendererFactory.CreateSelectionGizmoRenderers();
        public IDepthPicker CreateDepthPicker() => new MetalDepthPicker();

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
            {
                // Picking happens from input events after this render pass has ended.
                // MTKView may default depth to DontCare, which permits Metal to discard
                // it as soon as rendering completes, so explicitly preserve it.
                da.StoreAction = MTLStoreAction.Store;
                MetalFrameContext.SetDepthTexture(da.Texture);
            }

            var cmdBuffer = frame.CommandBuffer;
            if (cmdBuffer.NativePtr == IntPtr.Zero) return MetalFrameSession.Empty;

            var encoder = cmdBuffer.RenderCommandEncoder(frame.RenderPassDescriptor);
            if (encoder.NativePtr == IntPtr.Zero) return MetalFrameSession.Empty;

            MetalFrameContext.SetRenderCommandEncoder(encoder);
            return new MetalFrameSession(frame);
        }

        public void Resize(int width, int height) { }

        public void SetViewport(int x, int y, int width, int height)
        {
            MetalFrameState? frame = MetalFrameContext.CurrentFrame;
            if (frame == null || width <= 0 || height <= 0)
                return;

            MTLRenderCommandEncoder encoder = frame.RenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero)
                return;

            // ViewerController supplies OpenGL-style bottom-left coordinates. Metal's
            // viewport/scissor origin is top-left, so mirror Y against the attachment.
            var colorTexture = frame.RenderPassDescriptor.ColorAttachments.Object(0).Texture;
            int targetHeight = colorTexture.NativePtr == IntPtr.Zero ? height : checked((int)colorTexture.Height);
            int metalY = Math.Max(0, targetHeight - y - height);

            encoder.SetViewport(new MTLViewport
            {
                originX = x,
                originY = metalY,
                width = width,
                height = height,
                znear = 0.0,
                zfar = 1.0
            });
            encoder.SetScissorRect(new MTLScissorRect
            {
                x = (ulong)Math.Max(0, x),
                y = (ulong)metalY,
                width = (ulong)width,
                height = (ulong)height
            });
        }

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
