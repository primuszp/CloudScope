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
        public MetalRenderContext(MTLDevice device, MTLCommandQueue commandQueue)
        {
            if (device.NativePtr == IntPtr.Zero)
                throw new ArgumentException("A Metal device is required.", nameof(device));
            if (commandQueue.NativePtr == IntPtr.Zero)
                throw new ArgumentException("A Metal command queue is required.", nameof(commandQueue));

            Device = device;
            CommandQueue = commandQueue;
        }

        public MTLDevice Device { get; }

        public MTLCommandQueue CommandQueue { get; }

        /// <summary>The depth attachment of the frame last rendered, for depth picking.</summary>
        public MTLTexture DepthTexture { get; private set; }

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
            CurrentFrame = new MetalFrameState(renderPassDescriptor, drawable, commandBuffer);
        }

        public void SetDepthTexture(MTLTexture texture) => DepthTexture = texture;

        public void SetViewportSize(int width, int height)
        {
            if (width > 0 && height > 0)
                ViewportSize = (width, height);
        }

        public void SetRenderCommandEncoder(MTLRenderCommandEncoder encoder)
        {
            if (CurrentFrame != null)
                CurrentFrame.RenderCommandEncoder = encoder;
        }

        public void EndFrame() => CurrentFrame = null;
    }
}
