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
        [ThreadStatic] private static bool              _firstEncoderDone;

        public static MTKView?         CurrentView          => _currentView;
        public static MTLRenderPassDescriptor CurrentRenderPassDescriptor => _currentRenderPassDescriptor;
        public static CAMetalDrawable CurrentDrawable => _currentDrawable;
        public static MTLCommandBuffer CurrentCommandBuffer => _currentCommandBuffer;

        /// <summary>
        /// True after the first render-command-encoder of this frame has been
        /// created.  Subsequent encoders must use LoadAction.Load.
        /// </summary>
        public static bool FirstEncoderDone => _firstEncoderDone;

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
            _firstEncoderDone            = false;
        }

        /// <summary>Called by each renderer after it opens its render encoder.</summary>
        public static void MarkFirstEncoderDone() => _firstEncoderDone = true;

        public static void PrepareRenderPassForEncoder(MTLRenderPassDescriptor descriptor)
        {
            if (!_firstEncoderDone || descriptor.NativePtr == IntPtr.Zero)
                return;

            var color = descriptor.ColorAttachments.Object(0);
            if (color.NativePtr != IntPtr.Zero)
            {
                color.LoadAction = MTLLoadAction.Load;
                descriptor.ColorAttachments.SetObject(color, 0);
            }

            var depth = descriptor.DepthAttachment;
            if (depth.NativePtr != IntPtr.Zero)
                depth.LoadAction = MTLLoadAction.Load;
        }

        public static void End()
        {
            _currentView          = null;
            _currentRenderPassDescriptor = default;
            _currentDrawable = default;
            _currentCommandBuffer = default;
            _firstEncoderDone     = false;
        }
    }
}
