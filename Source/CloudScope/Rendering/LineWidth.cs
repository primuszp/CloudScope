namespace CloudScope.Rendering
{
    /// <summary>
    /// Line thickness rules shared by the backends.
    /// </summary>
    /// <remarks>
    /// A width above one pixel cannot be drawn with native lines: OpenGL core profile only
    /// guarantees 1.0 (macOS reports exactly [1, 1] and rejects anything wider, while Windows
    /// drivers usually accept more), and Metal has no line width at all. Anything wider is
    /// therefore drawn as a screen-space quad per segment, expanded in the vertex shader —
    /// the same technique three.js's Line2 and Filament use. This keeps gizmos identical on
    /// Windows, macOS, OpenGL and Metal.
    /// </remarks>
    public static class LineWidth
    {
        /// <summary>The widest line native line rendering is allowed to draw.</summary>
        public const float NativeMax = 1.0f;

        /// <summary>True when <paramref name="widthPixels"/> needs the quad-expanded path.</summary>
        public static bool NeedsExpansion(float widthPixels) => widthPixels > NativeMax + 0.01f;
    }
}
