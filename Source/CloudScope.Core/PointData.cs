using System.Runtime.InteropServices;

namespace CloudScope
{
    // GPU-ready point (6 floats = 24 bytes, matches VAO layout)
    [StructLayout(LayoutKind.Sequential)]
    public struct PointData
    {
        public float X, Y, Z;
        public float R, G, B;
    }
}
