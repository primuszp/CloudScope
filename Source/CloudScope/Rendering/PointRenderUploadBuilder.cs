using OpenTK.Mathematics;

namespace CloudScope.Rendering;

internal static class PointRenderUploadBuilder
{
    public const int DefaultPointsPerChunk = 1_000_000;

    public static void FillPoints(
        PointCloudRenderData data,
        Span<PointData> destination,
        int pointOffset = 0)
    {
        if (pointOffset < 0 || pointOffset + destination.Length > data.Count)
            throw new ArgumentOutOfRangeException(nameof(pointOffset));

        if (data.RenderOrder is not { Length: > 0 } renderOrder)
        {
            data.Points.AsSpan(pointOffset, destination.Length).CopyTo(destination);
            return;
        }

        for (int i = 0; i < destination.Length; i++)
            destination[i] = data.Points[renderOrder[pointOffset + i]];
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

    public static PointRenderChunk[] BuildChunks(
        PointCloudRenderData data,
        int pointCount,
        int pointsPerChunk = DefaultPointsPerChunk)
    {
        int chunkCount = GetChunkCount(pointCount, pointsPerChunk);
        var chunks = new PointRenderChunk[chunkCount];
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            int offset = chunkIndex * pointsPerChunk;
            int count = GetChunkPointCount(pointCount, chunkIndex, pointsPerChunk);
            Vector3 min = new(float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity);
            for (int i = 0; i < count; i++)
            {
                int orderedIndex = offset + i;
                int viewIndex = data.RenderOrder is { Length: > 0 } renderOrder
                    ? renderOrder[orderedIndex]
                    : orderedIndex;
                PointData point = data.Points[viewIndex];
                min = Vector3.ComponentMin(min, new Vector3(point.X, point.Y, point.Z));
                max = Vector3.ComponentMax(max, new Vector3(point.X, point.Y, point.Z));
            }
            chunks[chunkIndex] = new PointRenderChunk(offset, count, min, max);
        }
        return chunks;
    }
}
