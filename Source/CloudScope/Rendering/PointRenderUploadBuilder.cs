using System.Buffers;

namespace CloudScope.Rendering;

internal static class PointRenderUploadBuilder
{
    public const int DefaultPointsPerChunk = 1_000_000;

    /// <summary>Converts a run of the cloud into the packed vertices the GPU stores.</summary>
    public static void FillPoints(
        PointCloudRenderData data,
        Span<GpuPointVertex> destination,
        int pointOffset = 0,
        int[]? uploadOrder = null)
    {
        if (pointOffset < 0 || pointOffset + destination.Length > data.Count)
            throw new ArgumentOutOfRangeException(nameof(pointOffset));

        PointData[] points = data.Points;
        for (int i = 0; i < destination.Length; i++)
        {
            PointData point = points[ResolveViewIndex(data, pointOffset + i, uploadOrder)];
            destination[i] = new GpuPointVertex(point.X, point.Y, point.Z, point.R, point.G, point.B);
        }
    }

    /// <summary>The index into <see cref="PointCloudRenderData.Points"/> drawn at this slot.</summary>
    internal static int ResolveViewIndex(PointCloudRenderData data, int orderedIndex, int[]? uploadOrder)
    {
        if (uploadOrder is not null)
            return uploadOrder[orderedIndex];

        return data.RenderOrder is { Count: > 0 } renderOrder
            ? renderOrder.Resolve(orderedIndex)
            : orderedIndex;
    }


    public static int GetChunkCount(int pointCount, int pointsPerChunk = DefaultPointsPerChunk)
    {
        if (pointCount <= 0)
            return 0;
        if (pointsPerChunk <= 0)
            throw new ArgumentOutOfRangeException(nameof(pointsPerChunk));
        return (pointCount + pointsPerChunk - 1) / pointsPerChunk;
    }

    public static int GetChunkPointCount(
        int pointCount,
        int chunkIndex,
        int pointsPerChunk = DefaultPointsPerChunk)
    {
        int chunkCount = GetChunkCount(pointCount, pointsPerChunk);
        if ((uint)chunkIndex >= (uint)chunkCount)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        return Math.Min(pointsPerChunk, pointCount - chunkIndex * pointsPerChunk);
    }


}

