using System.Buffers;

namespace CloudScope.Rendering;

internal static class PointRenderUploadBuilder
{
    public const int DefaultPointsPerChunk = 1_000_000;

    /// <summary>
    /// Converts a run of the cloud into the packed vertices the GPU stores.
    /// </summary>
    /// <remarks>
    /// The upload order scatters the reads across the whole point array, so this is bound by
    /// memory latency rather than by arithmetic: at a hundred million points a single thread
    /// spends most of a minute waiting on cache misses. Splitting the run across cores
    /// overlaps those misses, which is where nearly all of the speed-up comes from.
    /// </remarks>
    public static unsafe void FillPoints(
        PointCloudRenderData data,
        Span<GpuPointVertex> destination,
        int pointOffset = 0,
        int[]? uploadOrder = null)
    {
        if (pointOffset < 0 || pointOffset + destination.Length > data.Count)
            throw new ArgumentOutOfRangeException(nameof(pointOffset));

        PointData[] points = data.Points;
        int length = destination.Length;
        fixed (GpuPointVertex* target = destination)
        {
            GpuPointVertex* output = target;
            ForEachPartition(length, (start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    PointData point = points[ResolveViewIndex(data, pointOffset + i, uploadOrder)];
                    output[i] = new GpuPointVertex(point.X, point.Y, point.Z, point.R, point.G, point.B);
                }
            });
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> over disjoint slices of <c>[0, length)</c>, in parallel
    /// once the run is large enough to pay for the threads.
    /// </summary>
    internal static void ForEachPartition(int length, Action<int, int> body)
    {
        const int MinimumParallelLength = 1 << 16;
        if (length <= MinimumParallelLength)
        {
            body(0, length);
            return;
        }

        int partitions = Math.Min(Environment.ProcessorCount, Math.Max(1, length / MinimumParallelLength));
        int perPartition = (length + partitions - 1) / partitions;
        Parallel.For(0, partitions, partition =>
        {
            int start = partition * perPartition;
            int end = Math.Min(start + perPartition, length);
            if (start < end)
                body(start, end);
        });
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

