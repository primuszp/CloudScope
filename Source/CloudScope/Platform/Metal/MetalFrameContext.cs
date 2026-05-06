namespace CloudScope.Platform.Metal
{
    internal static class MetalFrameContext
    {
#if MACOS
        [ThreadStatic]
        private static MetalKit.MTKView? currentView;

        [ThreadStatic]
        private static Metal.IMTLCommandBuffer? currentCommandBuffer;

        public static MetalKit.MTKView? CurrentView => currentView;
        public static Metal.IMTLCommandBuffer? CurrentCommandBuffer => currentCommandBuffer;

        public static void Begin(MetalKit.MTKView view, Metal.IMTLCommandBuffer commandBuffer)
        {
            currentView = view;
            currentCommandBuffer = commandBuffer;
        }

        public static void End()
        {
            currentCommandBuffer = null;
            currentView = null;
        }
#endif
    }
}
