using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
#if ENABLE_SHARPMETAL
using CloudScope.Platform.Metal;
#endif
using CloudScope.Platform.OpenGL;

namespace CloudScope.Rendering
{
    public static class RenderBackendFactory
    {
        public static IRenderBackend CreateDefault()
        {
            string? requested = Environment.GetEnvironmentVariable("CLOUDSCOPE_RENDER_BACKEND");
            if (string.Equals(requested, "opengl", StringComparison.OrdinalIgnoreCase))
                return Create(RenderBackendKind.OpenGL);
            if (string.Equals(requested, "metal", StringComparison.OrdinalIgnoreCase))
                return Create(RenderBackendKind.Metal);

#if ENABLE_SHARPMETAL
            // macOS-en Metal az alapértelmezett
            if (OperatingSystem.IsMacOS())
                return Create(RenderBackendKind.Metal);
#endif

            return Create(RenderBackendKind.OpenGL);
        }

        public static IRenderBackend Create(RenderBackendKind kind)
        {
            if (kind == RenderBackendKind.Metal)
            {
                if (!OperatingSystem.IsMacOS())
                    throw new PlatformNotSupportedException("Metal backend requires macOS.");
                return CreateMetal();
            }
            return kind switch
            {
                RenderBackendKind.OpenGL => new OpenGlRenderBackend(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        [SupportedOSPlatform("macos")]
#if ENABLE_SHARPMETAL
        private static IRenderBackend CreateMetal() => new MetalRenderBackend();
#else
        private static IRenderBackend CreateMetal() =>
            throw new PlatformNotSupportedException("This build was created without the Metal backend.");
#endif

        public static bool IsApplePlatform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS")) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST"));
    }
}
