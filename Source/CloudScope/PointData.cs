namespace CloudScope
{
    // GPU-ready point (6 floats = 24 bytes, matches VAO layout)
    public struct PointData
    {
        public float X, Y, Z;
        public float R, G, B;
    }
}

