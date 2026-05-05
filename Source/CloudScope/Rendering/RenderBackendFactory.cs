using System;
using System.Runtime.InteropServices;
using CloudScope.Platform.Metal;
using CloudScope.Platform.OpenGL;

namespace CloudScope.Rendering
{
    public static class RenderBackendFactory
    {
        public static IRenderBackend CreateDefault()
        {
            string? requested = Environment.GetEnvironmentVariable("CLOUDSCOPE_RENDER_BACKEND");
            if (string.Equals(requested, "metal", StringComparison.OrdinalIgnoreCase))
                return Create(RenderBackendKind.Metal);

            return Create(RenderBackendKind.OpenGL);
        }

        public static IRenderBackend Create(RenderBackendKind kind) => kind switch
        {
            RenderBackendKind.OpenGL => new OpenGlRenderBackend(),
            RenderBackendKind.Metal => new MetalRenderBackend(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        public static bool IsApplePlatform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS")) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST"));
    }
}
