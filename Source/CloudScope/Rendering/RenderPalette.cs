namespace CloudScope.Rendering
{
    /// <summary>
    /// Colors that must match across render backends, so an OpenGL frame and a Metal
    /// frame of the same scene look identical.
    /// </summary>
    public static class RenderPalette
    {
        /// <summary>Viewport background, as linear RGBA in the 0..1 range.</summary>
        public static (float R, float G, float B, float A) Background => (0.08f, 0.08f, 0.12f, 1f);
    }
}
