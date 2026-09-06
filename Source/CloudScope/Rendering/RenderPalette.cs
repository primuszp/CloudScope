using CloudScope.Ui;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Colors that must match across render backends, so an OpenGL frame and a Metal
    /// frame of the same scene look identical. Every value is a
    /// <see cref="UiPalette"/> token, so the 3D view and the shell around it are tuned from
    /// one place and cannot drift into different-looking versions of the same product.
    /// </summary>
    public static class RenderPalette
    {
        /// <summary>
        /// Viewport background as RGBA in the 0..1 range — the shell's
        /// <see cref="UiPalette.ViewportBackdrop"/>, so the 3D clear colour and the Avalonia
        /// surface behind it are the same neutral near-black and leave no seam.
        /// </summary>
        public static (float R, float G, float B, float A) Background => Rgba(UiPalette.ViewportBackdrop, 1f);

        /// <summary>Edge of the viewport tile that has focus, when the area is split.</summary>
        public static (float R, float G, float B, float A) ViewportBorderActive =>
            Rgba(UiPalette.ViewportBorderActive, 1f);

        /// <summary>Edge of a viewport tile that does not have focus.</summary>
        public static (float R, float G, float B, float A) ViewportBorderInactive =>
            Rgba(UiPalette.ViewportBorder, 0.9f);

        /// <summary>Width of a viewport tile's edge, in pixels.</summary>
        public static float ViewportBorderWidth => (float)UiPalette.ViewportBorderThickness;

        private static (float R, float G, float B, float A) Rgba(uint color, float alpha) => (
            UiPalette.R(color) / 255f,
            UiPalette.G(color) / 255f,
            UiPalette.B(color) / 255f,
            alpha);
    }
}
