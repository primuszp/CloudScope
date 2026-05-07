using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal;
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

            // macOS-en Metal az alapértelmezett
            if (OperatingSystem.IsMacOS())
                return Create(RenderBackendKind.Metal);

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
        private static IRenderBackend CreateMetal() => new MetalRenderBackend();

        public static bool IsApplePlatform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS")) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST"));
    }
}
