using CloudScope.Loading;

namespace CloudScope.Rendering;

internal static class PointRenderAttributeBuilder
{
    public static void Fill(
        PointCloudRenderData data,
        Span<PointRenderAttributeData> destination,
        int pointOffset = 0,
        int[]? uploadOrder = null)
    {
        var attributes = data.Attributes
            ?? throw new InvalidOperationException("Point render attributes are missing.");
        int[]? viewToSource = data.ViewToSource;

        double zSpan = attributes.MaxZ - attributes.MinZ;
        for (int i = 0; i < destination.Length; i++)
        {
            int orderedIndex = pointOffset + i;
            int viewIndex = uploadOrder is not null
                ? uploadOrder[orderedIndex]
                : data.RenderOrder is { Length: > 0 } renderOrder
                    ? renderOrder[orderedIndex]
                    : orderedIndex;
            int sourceIndex = viewToSource is null ? viewIndex : viewToSource[viewIndex];
            float zNormalized = zSpan > 0
                ? (float)((attributes.Z[sourceIndex] - attributes.MinZ) / zSpan)
                : 0.5f;
            zNormalized = Math.Clamp(zNormalized, 0f, 1f);
            float intensityNormalized = attributes.Intensity[sourceIndex] / 65535f;
            PointData rgbSource = data.SourcePoints is { } sourcePoints
                ? sourcePoints[sourceIndex]
                : data.Points[viewIndex];

            destination[i] = new PointRenderAttributeData(
                zNormalized,
                intensityNormalized,
                attributes.Class[sourceIndex],
                attributes.ReturnNumber[sourceIndex],
                rgbSource.R,
                rgbSource.G,
                rgbSource.B);
        }
    }

    public static int MapColorSource(ColorSource source) => source switch
    {
        ColorSource.Height => 1,
        ColorSource.Class => 2,
        ColorSource.Intensity => 3,
        ColorSource.Return => 4,
        _ => 0
    };
}
