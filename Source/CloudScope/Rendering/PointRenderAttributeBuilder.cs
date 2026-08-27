using CloudScope.Loading;

namespace CloudScope.Rendering;

internal static class PointRenderAttributeBuilder
{
    /// <summary>
    /// Converts a run of the cloud into the packed attributes the shader colors by.
    /// </summary>
    /// <remarks>
    /// Every point costs five scattered reads across five large arrays, so like
    /// <see cref="PointRenderUploadBuilder.FillPoints"/> this is memory-latency bound and is
    /// spread over the cores.
    /// </remarks>
    public static unsafe void Fill(
        PointCloudRenderData data,
        Span<GpuPointAttribute> destination,
        int pointOffset = 0,
        int[]? uploadOrder = null)
    {
        var attributes = data.Attributes
            ?? throw new InvalidOperationException("Point render attributes are missing.");
        int[]? viewToSource = data.ViewToSource;

        double zSpan = attributes.MaxZ - attributes.MinZ;
        int length = destination.Length;
        fixed (GpuPointAttribute* target = destination)
        {
            GpuPointAttribute* output = target;
            PointRenderUploadBuilder.ForEachPartition(length, (start, end) =>
            {
                for (int i = start; i < end; i++)
                    output[i] = Build(data, attributes, viewToSource, zSpan, pointOffset + i, uploadOrder);
            });
        }
    }

    private static GpuPointAttribute Build(
        PointCloudRenderData data,
        PointCloudAttributes attributes,
        int[]? viewToSource,
        double zSpan,
        int orderedIndex,
        int[]? uploadOrder)
    {
        int viewIndex = PointRenderUploadBuilder.ResolveViewIndex(data, orderedIndex, uploadOrder);
        int sourceIndex = viewToSource is null ? viewIndex : viewToSource[viewIndex];
        float zNormalized = zSpan > 0
            ? (float)((attributes.Z[sourceIndex] - attributes.MinZ) / zSpan)
            : 0.5f;
        zNormalized = Math.Clamp(zNormalized, 0f, 1f);
        float intensityNormalized = attributes.Intensity[sourceIndex] / 65535f;
        PointData rgbSource = data.SourcePoints is { } sourcePoints
            ? sourcePoints[sourceIndex]
            : data.Points[viewIndex];

        return new GpuPointAttribute(
            zNormalized,
            intensityNormalized,
            attributes.Class[sourceIndex],
            attributes.ReturnNumber[sourceIndex],
            rgbSource.R,
            rgbSource.G,
            rgbSource.B);
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
