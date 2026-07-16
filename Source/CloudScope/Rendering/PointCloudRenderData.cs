using CloudScope.Loading;

namespace CloudScope.Rendering
{
    public readonly record struct PointCloudRenderData(
        PointData[] Points,
        ProgressivePointOrder? RenderOrder = null,
        PointCloudAttributes? Attributes = null,
        int[]? ViewToSource = null,
        PointData[]? SourcePoints = null,
        ColorSource ColorSource = ColorSource.Rgb)
    {
        public static PointCloudRenderData Empty { get; } = new(Array.Empty<PointData>());

        public int Count => RenderOrder?.Count ?? Points.Length;
        public bool HasAttributes => Attributes != null;
        public bool HasSourceColors => SourcePoints != null;
    }
}
