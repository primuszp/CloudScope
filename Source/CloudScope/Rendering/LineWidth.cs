namespace CloudScope.Rendering
{
    /// <summary>
    /// Line thickness rules shared by the backends.
    /// </summary>
    /// <remarks>
    /// Native line support is not suitable for gizmos: OpenGL core profile only guarantees
    /// 1.0 (macOS reports exactly [1, 1]) and Metal has no configurable line width. Every
    /// gizmo line is therefore a screen-space quad expanded in the vertex shader — the same
    /// technique used by three.js's Line2 and Filament. This keeps antialiasing and thickness
    /// identical on Windows, macOS, OpenGL and Metal.
    /// </remarks>
    public static class LineWidth
    {
        /// <summary>The widest line native line rendering is allowed to draw.</summary>
        public const float NativeMax = 1.0f;

    /// <summary>
    /// Gizmo lines always use the quad-expanded path. This deliberately includes 1 px lines:
    /// macOS core OpenGL only guarantees that one native width, and native rasterisation makes
    /// the ghost pass look visibly different from the antialiased wide pass.
    /// </summary>
    public static bool NeedsExpansion(float widthPixels) => widthPixels > 0f;
    }
}
