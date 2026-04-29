namespace CloudScope.Loading
{
    public sealed record LoadedPointCloud(
        PointData[] Points,
        long LoadedCount,
        float Radius,
        bool HasColor,
        float ColorScale,
        double CenterX,
        double CenterY,
        double CenterZ);
}
