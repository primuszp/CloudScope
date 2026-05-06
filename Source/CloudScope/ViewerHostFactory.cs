using System;
using CloudScope.Rendering;

namespace CloudScope
{
    public static class ViewerHostFactory
    {
        public static IViewerHost Create(int width, int height)
        {
            var backend = RenderBackendFactory.CreateDefault();
            if (backend.Kind == RenderBackendKind.Metal)
                return CreateMetalHost(width, height, backend);

            return new PointCloudViewer(width, height, backend);
        }

        private static IViewerHost CreateMetalHost(int width, int height, IRenderBackend backend)
        {
#if MACOS
            return new Platform.Metal.MtkViewerHost(width, height, backend);
#else
            throw new PlatformNotSupportedException(
                "The Metal viewer host requires a macOS target. Use CLOUDSCOPE_RENDER_BACKEND=opengl on this build.");
#endif
        }
    }
}
