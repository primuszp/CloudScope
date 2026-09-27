namespace CloudScope.Loading;

/// <summary>Collects absolute coordinates before converting them into the GPU's local float frame.</summary>
internal sealed class PointCloudBuilder
{
    private readonly List<(double X, double Y, double Z, float R, float G, float B, ushort I)> _points = [];
    public int Count => _points.Count;
    public bool HasColor { get; set; }
    public void Add(double x, double y, double z, float r, float g, float b, ushort intensity)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            throw new InvalidDataException("Non-finite point coordinates.");
        _points.Add((x, y, z, r, g, b, intensity));
    }
    public LoadedPointCloud Build()
    {
        if (Count == 0) throw new InvalidDataException("File contains no valid points.");
        double minX = _points.Min(p => p.X), maxX = _points.Max(p => p.X);
        double minY = _points.Min(p => p.Y), maxY = _points.Max(p => p.Y);
        double minZ = _points.Min(p => p.Z), maxZ = _points.Max(p => p.Z);
        double cx = minX + (maxX - minX) / 2, cy = minY + (maxY - minY) / 2, cz = minZ + (maxZ - minZ) / 2;
        var points = new PointData[Count];
        var intensity = new ushort[Count];
        var z = new double[Count];
        for (int i = 0; i < Count; i++)
        {
            var p = _points[i];
            points[i] = new PointData { X = (float)(p.X - cx), Y = (float)(p.Y - cy), Z = (float)(p.Z - cz), R = p.R, G = p.G, B = p.B };
            intensity[i] = p.I; z[i] = p.Z;
            if (!HasColor)
            {
                float t = maxZ > minZ ? (float)((p.Z - minZ) / (maxZ - minZ)) : 0.5f;
                points[i].R = t; points[i].G = 1 - MathF.Abs(2 * t - 1); points[i].B = 1 - t;
            }
        }
        double dx = (maxX - minX) / 2, dy = (maxY - minY) / 2, dz = (maxZ - minZ) / 2;
        return new LoadedPointCloud(points, Count, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz), HasColor, 1, cx, cy, cz,
            new PointCloudAttributes(new byte[Count], intensity, new byte[Count], z, minZ, maxZ));
    }
}
