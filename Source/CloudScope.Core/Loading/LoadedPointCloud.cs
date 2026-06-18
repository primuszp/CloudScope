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
        double CenterZ,
        PointCloudAttributes Attributes)
    {
        public PointCloudDataset ToDataset()
        {
            if (LoadedCount > int.MaxValue)
                throw new InvalidOperationException($"Loaded point count {LoadedCount:N0} exceeds the in-memory viewer limit.");

            return new PointCloudDataset(Points, (int)LoadedCount, Radius, HasColor, ColorScale, Attributes);
        }
    }
}
