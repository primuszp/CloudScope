using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CloudScope.Library;

namespace CloudScope.Store;

/// <summary>A point on its way into the store: already in the packed layout the GPU reads.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StorePoint
{
    public const int Stride = GpuPointVertex.Stride + GpuPointAttribute.Stride;

    public GpuPointVertex Vertex;
    public GpuPointAttribute Attribute;
}

/// <summary>The cloud's extent, and the origin its stored float coordinates are measured from.</summary>
internal readonly record struct CloudBounds(
    double OriginX, double OriginY, double OriginZ,
    double MinX, double MinY, double MinZ,
    double MaxX, double MaxY, double MaxZ)
{
    public static CloudBounds FromHeader(LasHeader header) => new(
        (header.MinX + header.MaxX) * 0.5,
        (header.MinY + header.MaxY) * 0.5,
        (header.MinZ + header.MaxZ) * 0.5,
        header.MinX, header.MinY, header.MinZ,
        header.MaxX, header.MaxY, header.MaxZ);

    public float LocalMinX => (float)(MinX - OriginX);
    public float LocalMinY => (float)(MinY - OriginY);
    public float LocalMinZ => (float)(MinZ - OriginZ);
    public float LocalMaxX => (float)(MaxX - OriginX);
    public float LocalMaxY => (float)(MaxY - OriginY);
    public float LocalMaxZ => (float)(MaxZ - OriginZ);

    /// <summary>
    /// The cube the octree subdivides: a cube, so a cell stays a cube at every level and the
    /// sampling grid inside it stays isotropic.
    /// </summary>
    public (float Min, float Max)[] RootCube()
    {
        float sizeX = LocalMaxX - LocalMinX;
        float sizeY = LocalMaxY - LocalMinY;
        float sizeZ = LocalMaxZ - LocalMinZ;
        float size = MathF.Max(MathF.Max(sizeX, sizeY), MathF.Max(sizeZ, 1e-3f));
        float centreX = (LocalMinX + LocalMaxX) * 0.5f;
        float centreY = (LocalMinY + LocalMaxY) * 0.5f;
        float centreZ = (LocalMinZ + LocalMaxZ) * 0.5f;
        float half = size * 0.5f;
        return
        [
            (centreX - half, centreX + half),
            (centreY - half, centreY + half),
            (centreZ - half, centreZ + half)
        ];
    }
}

/// <summary>
/// Streams a LAS file as <see cref="StorePoint"/> values and places each on the survey grid.
/// </summary>
internal sealed class LasPointStream
{
    private const int SurveyResolution = 128;

    private readonly LasReader _reader;
    private readonly CloudBounds _bounds;
    private readonly LasPoint[] _batch;
    private readonly byte[] _raw;
    private readonly float _colorScale;
    private readonly double _zSpan;
    private readonly float _cubeMin;
    private readonly float _cubeSize;

    public LasPointStream(LasReader reader, CloudBounds bounds)
    {
        _reader = reader;
        _bounds = bounds;
        LasHeader header = reader.Header;
        HasAttributes = true;
        HasColor = header.PointDataFormatId is 2 or 3 or 5 or 7 or 8 or 10;
        _batch = new LasPoint[1 << 18];
        _raw = new byte[_batch.Length * header.PointDataRecordLength];
        _colorScale = 1f / 65535f;
        _zSpan = bounds.MaxZ - bounds.MinZ;

        (float Min, float Max)[] cube = bounds.RootCube();
        _cubeMin = cube[0].Min;
        _cubeSize = MathF.Max(cube[0].Max - cube[0].Min, 1e-6f);
        CubeMinY = cube[1].Min;
        CubeMinZ = cube[2].Min;
    }

    public bool HasAttributes { get; }
    public bool HasColor { get; }
    private float CubeMinY { get; }
    private float CubeMinZ { get; }

    /// <summary>Reads a run of points starting at <paramref name="startIndex"/>.</summary>
    public int Read(long startIndex, StorePoint[] destination)
    {
        int wanted = Math.Min(destination.Length, _batch.Length);
        int read = _reader.ReadBatch(startIndex, _batch, wanted, _raw);
        for (int i = 0; i < read; i++)
            destination[i] = Convert(_batch[i]);
        return read;
    }

    private StorePoint Convert(in LasPoint source)
    {
        float x = (float)(source.X - _bounds.OriginX);
        float y = (float)(source.Y - _bounds.OriginY);
        float z = (float)(source.Z - _bounds.OriginZ);

        float red, green, blue;
        if (HasColor)
        {
            red = source.R * _colorScale;
            green = source.G * _colorScale;
            blue = source.B * _colorScale;
        }
        else
        {
            // No color in the file: fall back to height, as the in-memory loader does.
            float height = _zSpan > 0 ? (float)((source.Z - _bounds.MinZ) / _zSpan) : 0.5f;
            red = height;
            green = 1f - MathF.Abs(2f * height - 1f);
            blue = 1f - height;
        }

        float zNormalized = _zSpan > 0
            ? (float)((source.Z - _bounds.MinZ) / _zSpan)
            : 0.5f;

        return new StorePoint
        {
            Vertex = new GpuPointVertex(x, y, z, red, green, blue),
            Attribute = new GpuPointAttribute(
                Math.Clamp(zNormalized, 0f, 1f),
                source.Intensity / 65535f,
                (byte)source.Classification,
                source.ReturnNumber,
                red, green, blue)
        };
    }

    /// <summary>Index of the survey cell a point falls in.</summary>
    public int SurveyCell(in StorePoint point)
    {
        int x = Axis(point.Vertex.X, _cubeMin);
        int y = Axis(point.Vertex.Y, CubeMinY);
        int z = Axis(point.Vertex.Z, CubeMinZ);
        return x + SurveyResolution * (y + SurveyResolution * z);
    }

    private int Axis(float value, float min)
    {
        int index = (int)((value - min) / _cubeSize * SurveyResolution);
        return Math.Clamp(index, 0, SurveyResolution - 1);
    }
}

/// <summary>
/// Splits the survey grid into chunks small enough to build in memory one at a time.
/// </summary>
/// <remarks>
/// The survey grid is walked as an octree and cut wherever a subtree holds no more than the
/// target. Dense regions therefore end up as small, deep chunks and empty regions cost
/// nothing, which is what keeps the peak memory of the build flat across very uneven clouds.
/// </remarks>
internal sealed class ChunkPlan
{
    private const int SurveyResolution = 128;
    private const int SurveyLevels = 7; // 2^7 == 128

    private readonly int[] _chunkOfCell;
    private readonly List<ChunkRegion> _regions;

    private ChunkPlan(int[] chunkOfCell, List<ChunkRegion> regions)
    {
        _chunkOfCell = chunkOfCell;
        _regions = regions;
    }

    public int ChunkCount => _regions.Count;

    /// <summary>The shallowest level any chunk sits at.</summary>
    public int ChunkLevel
    {
        get
        {
            int level = int.MaxValue;
            foreach (ChunkRegion region in _regions)
                level = Math.Min(level, region.Level);
            return level == int.MaxValue ? 0 : level;
        }
    }

    public int ChunkOf(int surveyCell) => _chunkOfCell[surveyCell];

    public int LevelOf(int chunkIndex) => _regions[chunkIndex].Level;

    /// <summary>The cube a chunk covers, in store-local coordinates.</summary>
    public (float Min, float Max)[] ChunkCube(int chunkIndex, CloudBounds bounds)
    {
        ChunkRegion region = _regions[chunkIndex];
        (float Min, float Max)[] root = bounds.RootCube();
        int span = 1 << region.Level;
        var cube = new (float Min, float Max)[3];
        Span<int> index = stackalloc int[3] { region.X, region.Y, region.Z };
        for (int axis = 0; axis < 3; axis++)
        {
            float size = (root[axis].Max - root[axis].Min) / span;
            float min = root[axis].Min + index[axis] * size;
            cube[axis] = (min, min + size);
        }

        return cube;
    }

    /// <summary>
    /// A build holds one file open per chunk while distributing, so the plan is re-cut with a
    /// larger target until it stays within a number of files any platform will allow.
    /// </summary>
    private const int MaximumChunks = 512;

    public static ChunkPlan FromSurvey(long[] surveyCounts, int chunkTargetPoints)
    {
        while (true)
        {
            var chunkOfCell = new int[surveyCounts.Length];
            var regions = new List<ChunkRegion>();
            Subdivide(surveyCounts, chunkOfCell, regions, 0, 0, 0, 0, chunkTargetPoints);
            if (regions.Count <= MaximumChunks || chunkTargetPoints >= int.MaxValue / 2)
                return new ChunkPlan(chunkOfCell, regions);

            chunkTargetPoints *= 2;
        }
    }

    private static long Subdivide(
        long[] counts,
        int[] chunkOfCell,
        List<ChunkRegion> regions,
        int level,
        int x, int y, int z,
        int chunkTargetPoints)
    {
        int span = SurveyResolution >> level;
        long total = 0;
        int originX = x * span, originY = y * span, originZ = z * span;
        for (int cz = 0; cz < span; cz++)
        for (int cy = 0; cy < span; cy++)
        for (int cx = 0; cx < span; cx++)
            total += counts[CellIndex(originX + cx, originY + cy, originZ + cz)];

        if (total == 0)
            return 0;

        // A region that fits in memory, or a single survey cell that does not fit but cannot
        // be split any further, becomes a chunk.
        if (total <= chunkTargetPoints || level == SurveyLevels)
        {
            int chunkIndex = regions.Count;
            regions.Add(new ChunkRegion(level, x, y, z));
            for (int cz = 0; cz < span; cz++)
            for (int cy = 0; cy < span; cy++)
            for (int cx = 0; cx < span; cx++)
                chunkOfCell[CellIndex(originX + cx, originY + cy, originZ + cz)] = chunkIndex;
            return total;
        }

        for (int octant = 0; octant < 8; octant++)
        {
            Subdivide(counts, chunkOfCell, regions, level + 1,
                x * 2 + (octant & 1),
                y * 2 + ((octant >> 1) & 1),
                z * 2 + ((octant >> 2) & 1),
                chunkTargetPoints);
        }

        return total;
    }

    private static int CellIndex(int x, int y, int z) =>
        x + SurveyResolution * (y + SurveyResolution * z);

    private readonly record struct ChunkRegion(int Level, int X, int Y, int Z);
}

/// <summary>Buffered append of the points belonging to one chunk.</summary>
internal sealed class ChunkWriter : IDisposable
{
    private const int BufferPoints = 1 << 14;

    private readonly FileStream _stream;
    private readonly StorePoint[] _buffer = new StorePoint[BufferPoints];
    private int _pending;

    public ChunkWriter(string path)
    {
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
    }

    public long Count { get; private set; }

    public void Add(in StorePoint point)
    {
        _buffer[_pending++] = point;
        Count++;
        if (_pending == _buffer.Length)
            Flush();
    }

    private void Flush()
    {
        if (_pending == 0)
            return;

        _stream.Write(MemoryMarshal.AsBytes(_buffer.AsSpan(0, _pending)));
        _pending = 0;
    }

    public static StorePoint[] ReadAll(string path, int count)
    {
        var points = new StorePoint[count];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        stream.ReadExactly(MemoryMarshal.AsBytes(points.AsSpan()));
        return points;
    }

    public void Dispose()
    {
        Flush();
        _stream.Dispose();
    }
}
