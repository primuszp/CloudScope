namespace CloudScope.Loading;

/// <summary>Shared, validated conversion from world doubles to the renderer's local floats.</summary>
internal readonly record struct PointCloudFrame(double X, double Y, double Z, float Radius)
{
    public static PointCloudFrame FromBounds(double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(minZ) ||
            !double.IsFinite(maxX) || !double.IsFinite(maxY) || !double.IsFinite(maxZ) ||
            minX > maxX || minY > maxY || minZ > maxZ)
            throw new InvalidDataException("Point cloud bounds must be finite and ordered.");
        // Halve first: adding same-sign world coordinates can overflow even when the
        // local cloud fits. Compute the radius in double to avoid float-square overflow.
        double x = minX / 2 + maxX / 2, y = minY / 2 + maxY / 2, z = minZ / 2 + maxZ / 2;
        double dx = maxX / 2 - minX / 2, dy = maxY / 2 - minY / 2, dz = maxZ / 2 - minZ / 2;
        double scale = Math.Max(dx, Math.Max(dy, dz));
        double radius = scale == 0 ? 0 : scale * Math.Sqrt(
            (dx / scale) * (dx / scale) + (dy / scale) * (dy / scale) + (dz / scale) * (dz / scale));
        if (!double.IsFinite(radius) || radius > float.MaxValue)
            throw new InvalidDataException("Point cloud extent exceeds the local rendering coordinate range.");
        return new(x, y, z, (float)radius);
    }

    public static float Local(double world, double origin)
    {
        float local = (float)(world - origin);
        if (!float.IsFinite(local))
            throw new InvalidDataException("Point coordinate exceeds the finite local rendering range.");
        return local;
    }
}
