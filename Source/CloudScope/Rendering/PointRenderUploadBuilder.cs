using OpenTK.Mathematics;
using System.Buffers;

namespace CloudScope.Rendering;

internal static class PointRenderUploadBuilder
{
    public const int DefaultPointsPerChunk = 1_000_000;
    private const int SpatialGridResolution = 4;
    private const int SpatialCellCount = SpatialGridResolution * SpatialGridResolution * SpatialGridResolution;

    public static void FillPoints(
        PointCloudRenderData data,
        Span<PointData> destination,
        int pointOffset = 0,
        int[]? uploadOrder = null)
    {
        if (pointOffset < 0 || pointOffset + destination.Length > data.Count)
            throw new ArgumentOutOfRangeException(nameof(pointOffset));

        if (uploadOrder is not null)
        {
            for (int i = 0; i < destination.Length; i++)
                destination[i] = data.Points[uploadOrder[pointOffset + i]];
            return;
        }

        if (data.RenderOrder is not { Length: > 0 } renderOrder)
        {
            data.Points.AsSpan(pointOffset, destination.Length).CopyTo(destination);
            return;
        }

        for (int i = 0; i < destination.Length; i++)
            destination[i] = data.Points[renderOrder[pointOffset + i]];
    }

    public static PointSpatialUploadLayout BuildSpatialLayout(PointCloudRenderData data, int pointCount)
    {
        if (pointCount <= 0)
            return PointSpatialUploadLayout.Empty;

        Vector3 cloudMin = new(float.PositiveInfinity);
        Vector3 cloudMax = new(float.NegativeInfinity);
        for (int i = 0; i < pointCount; i++)
        {
            PointData point = data.Points[ResolveViewIndex(data, i)];
            Vector3 position = new(point.X, point.Y, point.Z);
            cloudMin = Vector3.ComponentMin(cloudMin, position);
            cloudMax = Vector3.ComponentMax(cloudMax, position);
        }

        var counts = new int[SpatialCellCount];
        for (int i = 0; i < pointCount; i++)
        {
            PointData point = data.Points[ResolveViewIndex(data, i)];
            counts[GetCell(point, cloudMin, cloudMax)]++;
        }

        var offsets = new int[SpatialCellCount];
        var cursors = new int[SpatialCellCount];
        int runningOffset = 0;
        for (int cell = 0; cell < SpatialCellCount; cell++)
        {
            offsets[cell] = runningOffset;
            cursors[cell] = runningOffset;
            runningOffset += counts[cell];
        }

        int[] uploadOrder = ArrayPool<int>.Shared.Rent(pointCount);
        var cellMin = new Vector3[SpatialCellCount];
        var cellMax = new Vector3[SpatialCellCount];
        Array.Fill(cellMin, new Vector3(float.PositiveInfinity));
        Array.Fill(cellMax, new Vector3(float.NegativeInfinity));
        for (int i = 0; i < pointCount; i++)
        {
            int viewIndex = ResolveViewIndex(data, i);
            PointData point = data.Points[viewIndex];
            int cell = GetCell(point, cloudMin, cloudMax);
            uploadOrder[cursors[cell]++] = viewIndex;
            Vector3 position = new(point.X, point.Y, point.Z);
            cellMin[cell] = Vector3.ComponentMin(cellMin[cell], position);
            cellMax[cell] = Vector3.ComponentMax(cellMax[cell], position);
        }

        var chunks = new PointRenderChunk[counts.Count(count => count > 0)];
        int chunkIndex = 0;
        for (int cell = 0; cell < SpatialCellCount; cell++)
        {
            if (counts[cell] == 0)
                continue;
            chunks[chunkIndex++] = new PointRenderChunk(offsets[cell], counts[cell], cellMin[cell], cellMax[cell]);
        }

        return new PointSpatialUploadLayout(uploadOrder, pointCount, chunks);
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

    private static int ResolveViewIndex(PointCloudRenderData data, int orderedIndex) =>
        data.RenderOrder is { Length: > 0 } renderOrder ? renderOrder[orderedIndex] : orderedIndex;

    private static int GetCell(PointData point, Vector3 min, Vector3 max)
    {
        int x = GetAxisCell(point.X, min.X, max.X);
        int y = GetAxisCell(point.Y, min.Y, max.Y);
        int z = GetAxisCell(point.Z, min.Z, max.Z);
        return x + SpatialGridResolution * (y + SpatialGridResolution * z);
    }

    private static int GetAxisCell(float value, float min, float max)
    {
        float span = max - min;
        if (!(span > 0f) || !float.IsFinite(span))
            return 0;
        int cell = (int)((value - min) / span * SpatialGridResolution);
        return Math.Clamp(cell, 0, SpatialGridResolution - 1);
    }
}

internal sealed class PointSpatialUploadLayout : IDisposable
{
    public static PointSpatialUploadLayout Empty { get; } = new(null, 0, Array.Empty<PointRenderChunk>());

    private int[]? _uploadOrder;

    public PointSpatialUploadLayout(int[]? uploadOrder, int count, PointRenderChunk[] chunks)
    {
        _uploadOrder = uploadOrder;
        Count = count;
        Chunks = chunks;
    }

    public int[]? UploadOrder => _uploadOrder;
    public int Count { get; }
    public PointRenderChunk[] Chunks { get; }

    public void Dispose()
    {
        int[]? uploadOrder = Interlocked.Exchange(ref _uploadOrder, null);
        if (uploadOrder is not null)
            ArrayPool<int>.Shared.Return(uploadOrder);
    }
}
