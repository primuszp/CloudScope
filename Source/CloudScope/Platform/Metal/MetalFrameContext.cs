using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    /// <summary>
    /// Thread-local Metal frame state: device, command queue,
    /// current MTKView and command buffer for each draw call.
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal static class MetalFrameContext
    {
        // ── Persistent (set once at startup) ────────────────────────────────────
        public static MTLDevice Device { get; private set; }
        public static MTLCommandQueue CommandQueue { get; private set; }

        // ── Per-frame ────────────────────────────────────────────────────────────
        [ThreadStatic] private static MTKView? _currentView;
        [ThreadStatic] private static MTLCommandBuffer _currentCommandBuffer;

        public static MTKView? CurrentView => _currentView;
        public static MTLCommandBuffer CurrentCommandBuffer => _currentCommandBuffer;

        // ── Startup ──────────────────────────────────────────────────────────────
        public static void Initialize(MTLDevice device, MTLCommandQueue commandQueue)
        {
            Device = device;
            CommandQueue = commandQueue;
        }

        // ── Frame lifecycle ──────────────────────────────────────────────────────
        public static void Begin(MTKView view, MTLCommandBuffer commandBuffer)
        {
            _currentView = view;
            _currentCommandBuffer = commandBuffer;
        }

        public static void End()
        {
            _currentView = null;
            _currentCommandBuffer = default;
        }
    }
}
