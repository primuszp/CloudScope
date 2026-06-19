using OpenTK.Mathematics;

namespace CloudScope.Loading
{
    /// <summary>
    /// Shared classification-code → color palette used by both the point-cloud
    /// "color by class" rendering and the label registry, so a label drawn with a
    /// given class code always matches the COLORBY Class view.
    /// </summary>
    public static class ClassColorPalette
    {
        private static readonly uint[] Colors =
        [
            0x808080, 0xD0D0D0, 0x8B5A2B, 0x7CFC00,
            0x32CD32, 0x006400, 0xB22222, 0xFF00FF,
            0xA9A9A9, 0x1E90FF, 0x696969, 0x303030,
            0x808000, 0xFFD700, 0xFFA500, 0xFF4500
        ];

        public static Vector3 GetColor(byte code)
        {
            uint color = Colors[code % Colors.Length];
            return new Vector3(
                ((color >> 16) & 0xFF) / 255f,
                ((color >> 8) & 0xFF) / 255f,
                (color & 0xFF) / 255f);
        }
    }
}
