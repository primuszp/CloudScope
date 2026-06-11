using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Labeling
{
    internal static class LabelColorPalette
    {
        private static readonly Dictionary<string, Vector3> Palette = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ground"]     = new Vector3(0.55f, 0.27f, 0.07f),  // brown
            ["Building"]   = new Vector3(1.0f,  0.27f, 0.27f),  // red
            ["Vegetation"] = new Vector3(0.13f, 0.80f, 0.13f),  // green
            ["Vehicle"]    = new Vector3(1.0f,  0.84f, 0.0f),   // gold
            ["Road"]       = new Vector3(0.60f, 0.60f, 0.60f),  // gray
            ["Water"]      = new Vector3(0.12f, 0.56f, 1.0f),   // blue
            ["Wire"]       = new Vector3(0.93f, 0.51f, 0.93f),  // violet
        };

        public static Vector3 GetColor(string label)
        {
            if (Palette.TryGetValue(label, out var c)) return c;
            int h = label.GetHashCode();
            float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
            float s = 0.75f, v = 0.9f;
            int hi = (int)(hue * 6f) % 6;
            float f = hue * 6f - hi;
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            return hi switch
            {
                0 => new Vector3(v, t, p),
                1 => new Vector3(q, v, p),
                2 => new Vector3(p, v, t),
                3 => new Vector3(p, q, v),
                4 => new Vector3(t, p, v),
                _ => new Vector3(v, p, q),
            };
        }
    }
}
