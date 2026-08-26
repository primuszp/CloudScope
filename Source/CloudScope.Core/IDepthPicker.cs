namespace CloudScope
{
    /// <summary>
    /// Reads back the depth buffer of the frame that was last rendered.
    /// </summary>
    /// <remarks>
    /// Coordinates and results follow the OpenGL convention every backend is normalized to:
    /// the origin is the bottom-left of the viewport, and depth is window depth in 0..1.
    /// </remarks>
    public interface IDepthPicker
    {
        /// <summary>Depth at a single pixel; 1 when nothing was drawn there.</summary>
        float ReadDepth(int x, int y);

        /// <summary>
        /// Fills <paramref name="destination"/> with a rectangle of depth values, row-major and
        /// bottom-up: element 0 is the bottom-left pixel of the requested window, exactly as
        /// <c>glReadPixels</c> returns it.
        /// </summary>
        /// <returns>The number of values written.</returns>
        int ReadDepthWindow(int x, int y, int width, int height, float[] destination);
    }
}
