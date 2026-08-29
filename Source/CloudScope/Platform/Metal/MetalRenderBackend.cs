using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class MetalRenderBackend : IRenderBackend
    {
        private readonly MetalRenderContext _context;

        /// <summary>Creates a backend that renders with the given device and queue.</summary>
        public MetalRenderBackend(MTLDevice device, MTLCommandQueue commandQueue)
        {
            _context = new MetalRenderContext(device, commandQueue, ChooseSampleCount(device));
        }

        /// <summary>Creates a backend on the system default device, with a queue of its own.</summary>
        public static MetalRenderBackend CreateWithSystemDefaultDevice()
        {
            MTLDevice device = MTLDevice.CreateSystemDefaultDevice();
            if (device.NativePtr == IntPtr.Zero)
                throw new InvalidOperationException("No Metal device is available.");

            return new MetalRenderBackend(device, device.NewCommandQueue());
        }

        public RenderBackendKind Kind => RenderBackendKind.Metal;

        /// <summary>The device this backend renders with; hosts create their view against it.</summary>
        public MTLDevice Device => _context.Device;
        public int SampleCount => _context.SampleCount;

        /// <summary>Sets physical pixels per logical UI pixel for Retina-sized overlays.</summary>
        public void SetDisplayScale(float scale) => _context.SetDisplayScale(scale);

        public IPointCloudRenderer  CreatePointCloudRenderer()  => new MetalPointCloudRenderer(_context);
        public IPointTileCloudRenderer CreateStreamingPointCloudRenderer() => new MetalStreamingPointCloudRenderer(_context);
        public IHighlightRenderer   CreateHighlightRenderer()   => new MetalHighlightRenderer(_context);
        public IOverlayRenderer     CreateOverlayRenderer()     => new MetalOverlayRenderer(_context);
        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => new(new MetalBoxGizmoRenderer(_context),
                   new MetalSphereGizmoRenderer(_context),
                   new MetalCylinderGizmoRenderer(_context));
        public IDepthPicker CreateDepthPicker() => new MetalDepthPicker(_context);

        public void Initialize()
        {
            // Metal carries depth test, blending and point size in the pipeline and
            // depth-stencil states each renderer builds, so there is no global state to seed.
        }

        /// <summary>
        /// Hands the backend the drawable the host just acquired. The next
        /// <see cref="BeginFrame"/> records into it.
        /// </summary>
        public void PrepareFrame(
            MTLRenderPassDescriptor renderPassDescriptor,
            CAMetalDrawable drawable,
            MTLCommandBuffer commandBuffer)
            => _context.BeginFrame(renderPassDescriptor, drawable, commandBuffer);

        public IRenderFrameSession BeginFrame()
        {
            var frame = _context.CurrentFrame;
            if (frame == null || frame.RenderPassDescriptor.NativePtr == IntPtr.Zero)
                return new MetalFrameSession(_context, null);

            var da = frame.RenderPassDescriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero && da.Texture.NativePtr != IntPtr.Zero)
            {
                // Picking happens from input events after this render pass has ended.
                // MTKView may default depth to DontCare, which permits Metal to discard
                // it as soon as rendering completes, so explicitly preserve it.
                if (_context.SampleCount > 1 && da.Texture.SampleCount > 1)
                {
                    MTLTexture resolved = _context.EnsureDepthResolveTexture(
                        checked((int)da.Texture.Width), checked((int)da.Texture.Height), da.Texture.PixelFormat);
                    da.ResolveTexture = resolved;
                    da.DepthResolveFilter = MTLMultisampleDepthResolveFilter.Min;
                    da.StoreAction = MTLStoreAction.MultisampleResolve;
                    _context.SetDepthTexture(resolved);
                }
                else
                {
                    da.StoreAction = MTLStoreAction.Store;
                    _context.SetDepthTexture(da.Texture);
                }
            }

            var cmdBuffer = frame.CommandBuffer;
            if (cmdBuffer.NativePtr == IntPtr.Zero)
                return new MetalFrameSession(_context, null);

            var encoder = cmdBuffer.RenderCommandEncoder(frame.RenderPassDescriptor);
            if (encoder.NativePtr == IntPtr.Zero)
                return new MetalFrameSession(_context, null);

            _context.SetRenderCommandEncoder(encoder);
            return new MetalFrameSession(_context, frame);
        }

        public void Resize(int width, int height) { }

        private static int ChooseSampleCount(MTLDevice device)
        {
            if (device.SupportsTextureSampleCount(4)) return 4;
            if (device.SupportsTextureSampleCount(2)) return 2;
            return 1;
        }

        public void SetViewport(int x, int y, int width, int height)
        {
            MetalFrameState? frame = _context.CurrentFrame;
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

            _context.SetViewportSize(width, height);
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
            private readonly MetalRenderContext _context;
            private readonly MetalFrameState? _frame;
            private bool _disposed;

            public MetalFrameSession(MetalRenderContext context, MetalFrameState? frame)
            {
                _context = context;
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

                _context.EndFrame();
            }
        }
    }
}
