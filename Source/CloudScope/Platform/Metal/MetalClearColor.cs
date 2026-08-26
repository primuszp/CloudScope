using System.Runtime.Versioning;
using CloudScope.Rendering;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    /// <summary>Bridges <see cref="RenderPalette"/> to Metal's clear color type.</summary>
    [SupportedOSPlatform("macos")]
    internal static class MetalClearColor
    {
        /// <summary>The shared viewport background, so Metal clears to the same color OpenGL does.</summary>
        public static MTLClearColor FromPalette()
        {
            (float r, float g, float b, float a) = RenderPalette.Background;
            return new MTLClearColor { red = r, green = g, blue = b, alpha = a };
        }
    }
}
