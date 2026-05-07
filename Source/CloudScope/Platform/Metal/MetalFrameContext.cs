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
        [ThreadStatic] private static MTKView?          _currentView;
        [ThreadStatic] private static MTLRenderPassDescriptor _currentRenderPassDescriptor;
        [ThreadStatic] private static CAMetalDrawable   _currentDrawable;
        [ThreadStatic] private static MTLCommandBuffer  _currentCommandBuffer;
        [ThreadStatic] private static MTLRenderCommandEncoder _currentRenderCommandEncoder;

        public static MTKView?         CurrentView          => _currentView;
        public static MTLRenderPassDescriptor CurrentRenderPassDescriptor => _currentRenderPassDescriptor;
        public static CAMetalDrawable CurrentDrawable => _currentDrawable;
        public static MTLCommandBuffer CurrentCommandBuffer => _currentCommandBuffer;
        
        public static MTLRenderCommandEncoder CurrentRenderCommandEncoder
        {
            get => _currentRenderCommandEncoder;
            set => _currentRenderCommandEncoder = value;
        }

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
            _currentView                 = view;
            _currentRenderPassDescriptor = renderPassDescriptor;
            _currentDrawable             = drawable;
            _currentCommandBuffer        = commandBuffer;
            _currentRenderCommandEncoder = default;
        }

        public static void End()
        {
            if (_currentRenderCommandEncoder.NativePtr != IntPtr.Zero)
            {
                _currentRenderCommandEncoder.EndEncoding();
            }
            _currentView          = null;
            _currentRenderPassDescriptor = default;
            _currentDrawable = default;
            _currentCommandBuffer = default;
            _currentRenderCommandEncoder = default;
        }
    }
}
