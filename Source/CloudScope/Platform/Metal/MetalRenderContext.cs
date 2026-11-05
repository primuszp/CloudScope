using System;
using System.Runtime.Versioning;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace CloudScope.Platform.Metal
{
    /// <summary>
    /// The Metal device, queue and in-flight frame that one <see cref="MetalRenderBackend"/>
    /// and the renderers it creates share. It is owned by the backend and handed to each
    /// renderer at construction, so two viewers on two devices cannot see each other's state.
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal sealed class MetalRenderContext
    {
        public MetalRenderContext(MTLDevice device, MTLCommandQueue commandQueue, int sampleCount)
        {
            if (device.NativePtr == IntPtr.Zero)
                throw new ArgumentException("A Metal device is required.", nameof(device));
            if (commandQueue.NativePtr == IntPtr.Zero)
                throw new ArgumentException("A Metal command queue is required.", nameof(commandQueue));

            Device = device;
            CommandQueue = commandQueue;
            SampleCount = Math.Max(sampleCount, 1);
            _buffers = new(CreateDrawBuffer,
                buffer => checked((int)buffer.Length), buffer => MetalResources.Release(buffer.NativePtr));
        }

        private MTLBuffer CreateDrawBuffer(int size)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            MTLBuffer buffer = Device.NewBuffer((ulong)size, MTLResourceOptions.ResourceStorageModeShared);
            if (buffer.NativePtr == IntPtr.Zero) throw new OutOfMemoryException($"Metal could not allocate {size:N0} draw bytes.");
            return buffer;
        }

        private readonly CloudScope.Rendering.FrameResourcePool<MTLBuffer> _buffers;
        private long _sequence;
        public MTLDevice Device { get; }

        public MTLCommandQueue CommandQueue { get; }

        /// <summary>Raster samples shared by every Metal pipeline and render attachment.</summary>
        public int SampleCount { get; }

        /// <summary>
        /// Physical pixels per logical UI pixel. Metal point sizes are expressed in physical
        /// pixels, so screen-space markers use this to retain their intended size on Retina.
        /// </summary>
        public float DisplayScale { get; private set; } = 1f;

        /// <summary>The depth attachment of the frame last rendered, for depth picking.</summary>
        public MTLTexture DepthTexture { get; private set; }
        private MTLTexture _resolvedDepthTexture;

        /// <summary>Size of the active viewport in pixels, for screen-space shader math.</summary>
        public (int Width, int Height) ViewportSize { get; private set; } = (1, 1);

        /// <summary>The frame currently being recorded, or <c>null</c> between frames.</summary>
        public MetalFrameState? CurrentFrame { get; private set; }

        /// <summary>Starts recording a frame against the drawable the host just acquired.</summary>
        public void BeginFrame(
            MTLRenderPassDescriptor renderPassDescriptor,
            CAMetalDrawable drawable,
            MTLCommandBuffer commandBuffer)
        {
            _buffers.BeginFrame();
            CurrentFrame = new MetalFrameState(renderPassDescriptor, drawable, commandBuffer)
            { Sequence = ++_sequence, CompletedSequence = _buffers.CompletedSequence };
        }

        public void SetDepthTexture(MTLTexture texture) => DepthTexture = texture;

        /// <summary>
        /// Gets a single-sample depth target matching the current drawable. A multisample depth
        /// attachment cannot be copied directly for picking, so the render pass resolves into
        /// this texture at the end of the frame.
        /// </summary>
        public MTLTexture EnsureDepthResolveTexture(int width, int height, MTLPixelFormat format)
        {
            if (_resolvedDepthTexture.NativePtr != IntPtr.Zero
                && _resolvedDepthTexture.Width == (ulong)width
                && _resolvedDepthTexture.Height == (ulong)height
                && _resolvedDepthTexture.PixelFormat == format)
                return _resolvedDepthTexture;

            MetalResources.Release(_resolvedDepthTexture.NativePtr);
            _resolvedDepthTexture = default;
            var descriptor = MTLTextureDescriptor.Texture2DDescriptor(format, (ulong)width, (ulong)height, false);
            descriptor.Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
            descriptor.StorageMode = MTLStorageMode.Private;
            _resolvedDepthTexture = Device.NewTexture(descriptor);
            return _resolvedDepthTexture;
        }

        public void SetViewportSize(int width, int height)
        {
            if (width > 0 && height > 0)
                ViewportSize = (width, height);
        }

        public void SetDisplayScale(float scale)
        {
            if (float.IsFinite(scale) && scale > 0f)
                DisplayScale = scale;
        }

        public void SetRenderCommandEncoder(MTLRenderCommandEncoder encoder)
        {
            if (CurrentFrame != null)
                CurrentFrame.RenderCommandEncoder = encoder;
        }

        public unsafe MTLBuffer UploadFrameBuffer(float[] vertices)
        {
            int bytes = checked(vertices.Length * sizeof(float));
            MTLBuffer buffer = _buffers.Rent(bytes);
            fixed (float* source = vertices) Buffer.MemoryCopy(source, buffer.Contents.ToPointer(), (long)buffer.Length, bytes);
            return buffer;
        }

        public void EndFrame(bool submitted)
        {
            if (submitted && CurrentFrame is { } frame)
            {
                var command = frame.CommandBuffer;
                CloudScope.Platform.MacOS.ObjC.ObjectiveCRuntime.Retain(command.NativePtr);
                _buffers.Submit(frame.Sequence,
                    () => command.Status is MTLCommandBufferStatus.Completed or MTLCommandBufferStatus.Error,
                    command.WaitUntilCompleted,
                    () => MetalResources.Release(command.NativePtr));
            }
            else _buffers.CancelFrame();
            CurrentFrame = null;
        }

        public string ResourceDiagnostics => $"GPU buffers   {_buffers.CreatedResources:N0} created; {_buffers.CachedBytes:N0} cached bytes; {_buffers.InFlightCount} in flight";
        public void WaitForIdle() => _buffers.WaitForIdle();
        public void Dispose()
        {
            _buffers.Dispose();
            MetalResources.Release(_resolvedDepthTexture.NativePtr);
            _resolvedDepthTexture = default;
        }
    }
}
