using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    internal static class MetalFrameContext
    {
        // ── Persistent ───────────────────────────────────────────────────────────
        public static MTLDevice       Device       { get; private set; }
        public static MTLCommandQueue CommandQueue { get; private set; }
        public static MTLTexture      DepthTexture { get; private set; }

        // ── Per-frame (ThreadStatic so the render thread has its own slot) ───────
        [ThreadStatic] private static MetalFrameState? _currentFrame;

        public static MetalFrameState? CurrentFrame => _currentFrame;
        public static void Initialize(MTLDevice device, MTLCommandQueue commandQueue)
        {
            Device       = device;
            CommandQueue = commandQueue;
        }

        public static void SetDepthTexture(MTLTexture texture) => DepthTexture = texture;

        public static void Begin(
            MTKView view,
            MTLRenderPassDescriptor renderPassDescriptor,
            CAMetalDrawable drawable,
            MTLCommandBuffer commandBuffer)
        {
            _currentFrame = new MetalFrameState(view, renderPassDescriptor, drawable, commandBuffer);
        }

        public static void SetRenderCommandEncoder(MTLRenderCommandEncoder encoder)
        {
            if (_currentFrame != null)
                _currentFrame.RenderCommandEncoder = encoder;
        }

        public static void End()
        {
            _currentFrame = null;
        }
    }
}
