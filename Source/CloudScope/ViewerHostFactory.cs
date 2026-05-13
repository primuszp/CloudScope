using System;
using System.Runtime.Versioning;
using CloudScope.Rendering;

namespace CloudScope
{
    public static class ViewerHostFactory
    {
        public static IViewerHost Create(int width, int height)
        {
            var backend = RenderBackendFactory.CreateDefault();
            if (backend.Kind == RenderBackendKind.Metal && OperatingSystem.IsMacOS())
                return CreateMetalHost(width, height, backend);

            return new PointCloudViewer(width, height, backend);
        }

        [SupportedOSPlatform("macos")]
        private static IViewerHost CreateMetalHost(int width, int height, IRenderBackend backend)
        {
            if (!OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException(
                    "The Metal viewer host requires macOS. Use CLOUDSCOPE_RENDER_BACKEND=opengl on this platform.");

#if ENABLE_SHARPMETAL
            return new Platform.Metal.SharpMetalViewerHost(width, height, backend);
#else
            throw new PlatformNotSupportedException("This build was created without the SharpMetal viewer host.");
#endif
        }
    }
}
