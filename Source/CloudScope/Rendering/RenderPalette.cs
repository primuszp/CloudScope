using CloudScope.Ui;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Colors that must match across render backends, so an OpenGL frame and a Metal
    /// frame of the same scene look identical.
    /// </summary>
    public static class RenderPalette
    {
        /// <summary>
        /// Viewport background as RGBA in the 0..1 range. It is the shell's
        /// <see cref="UiPalette.ViewportBackdrop"/> token — one neutral near-black, no hue —
        /// so the 3D clear colour and the Avalonia surface behind it are the same value and
        /// tuning the token moves both.
        /// </summary>
        public static (float R, float G, float B, float A) Background => (
            UiPalette.R(UiPalette.ViewportBackdrop) / 255f,
            UiPalette.G(UiPalette.ViewportBackdrop) / 255f,
            UiPalette.B(UiPalette.ViewportBackdrop) / 255f,
            1f);
    }
}
