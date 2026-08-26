using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace CloudScope.Store;

/// <summary>
/// One cell of an on-disk cloud. Its points occupy a contiguous run of the point file, so a
/// cell is read with a single copy and uploaded as one GPU buffer.
/// </summary>
/// <remarks>
/// Bounds are floats relative to <see cref="PointTileStoreHeader.Origin"/>, matching how the
/// points themselves are stored.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct PointTileNode
{
    public const int Stride = 48;

    public PointTileNode(
        float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ,
        long pointOffset, int pointCount, int firstChild, int childCount, byte level)
    {
        MinX = minX; MinY = minY; MinZ = minZ;
        MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        PointOffset = pointOffset;
        PointCount = pointCount;
        FirstChild = firstChild;
        ChildCount = childCount;
        Level = level;
        Padding0 = 0;
        Padding1 = 0;
    }

    public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

    /// <summary>Index of the cell's first point in the point file.</summary>
    public readonly long PointOffset;

    /// <summary>Points held by this cell itself, not counting its children.</summary>
    public readonly int PointCount;

    /// <summary>Index of the first child in the node table, or -1 for a leaf.</summary>
    /// <remarks>Children are contiguous, which is why they need only a start and a count.</remarks>
    public readonly int FirstChild;

    /// <summary>
    /// How many children follow <see cref="FirstChild"/>.
    /// </summary>
    /// <remarks>
    /// Not capped at eight: a build may attach a cell several levels below its parent where
    /// the levels in between held no points of their own, so a parent can have many children.
    /// Each child carries its own bounds and level, so the tree stays well defined.
    /// </remarks>
    public readonly int ChildCount;

    /// <summary>Depth below the root.</summary>
    public readonly byte Level;

    private readonly byte Padding0;
    private readonly ushort Padding1;

    public bool IsLeaf => ChildCount == 0;

    /// <summary>
    /// The file format hard-codes the record size, so a change to the fields that the
    /// compiler lays out differently would silently write unreadable stores.
    /// </summary>
    static PointTileNode()
    {
        if (System.Runtime.CompilerServices.Unsafe.SizeOf<PointTileNode>() != Stride)
            throw new InvalidOperationException(
                $"{nameof(PointTileNode)} is {System.Runtime.CompilerServices.Unsafe.SizeOf<PointTileNode>()} bytes, "
                + $"but the store format expects {Stride}.");
    }
}

/// <summary>Fixed-size preamble of a store's index file.</summary>
public readonly record struct PointTileStoreHeader(
    long PointCount,
    int NodeCount,
    int MaxLevel,
    bool HasAttributes,
    double OriginX,
    double OriginY,
    double OriginZ,
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ)
{
    public const int Stride = 128;
    public const uint Magic = 0x53544343; // "CCTS"
    public const int Version = 1;

    /// <summary>
    /// World position the stored float coordinates are measured from.
    /// </summary>
    /// <remarks>
    /// Survey coordinates run into the millions of metres, where a float resolves to about
    /// six centimetres. Storing offsets from the cloud's own centre keeps the stored floats
    /// in the tens of thousands, where they resolve to well under a millimetre.
    /// </remarks>
    public (double X, double Y, double Z) Origin => (OriginX, OriginY, OriginZ);

    public void Write(Span<byte> destination)
    {
        destination[..Stride].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], Version);
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], PointCount);
        BinaryPrimitives.WriteInt32LittleEndian(destination[16..], NodeCount);
        BinaryPrimitives.WriteInt32LittleEndian(destination[20..], MaxLevel);
        BinaryPrimitives.WriteInt32LittleEndian(destination[24..], HasAttributes ? 1 : 0);
        Span<double> values = stackalloc double[9]
        {
            OriginX, OriginY, OriginZ, MinX, MinY, MinZ, MaxX, MaxY, MaxZ
        };
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteDoubleLittleEndian(destination[(32 + i * 8)..], values[i]);
    }

    public static PointTileStoreHeader Read(ReadOnlySpan<byte> source)
    {
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (magic != Magic)
            throw new InvalidDataException("Not a CloudScope tile store.");

        int version = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        if (version != Version)
            throw new InvalidDataException($"Tile store version {version} is not supported.");

        return new PointTileStoreHeader(
            BinaryPrimitives.ReadInt64LittleEndian(source[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(source[16..]),
            BinaryPrimitives.ReadInt32LittleEndian(source[20..]),
            BinaryPrimitives.ReadInt32LittleEndian(source[24..]) != 0,
            ReadValue(source, 0), ReadValue(source, 1), ReadValue(source, 2),
            ReadValue(source, 3), ReadValue(source, 4), ReadValue(source, 5),
            ReadValue(source, 6), ReadValue(source, 7), ReadValue(source, 8));
    }

    private static double ReadValue(ReadOnlySpan<byte> source, int index) =>
        BinaryPrimitives.ReadDoubleLittleEndian(source[(32 + index * 8)..]);
}

/// <summary>
/// A point cloud indexed on disk: an octree whose cells hold GPU-ready points.
/// </summary>
/// <remarks>
/// This is what makes a cloud larger than memory renderable. The node table is small enough
/// to keep resident - a few dozen bytes per cell - while the points stay on disk and are
/// mapped in only for the cells the camera actually needs. The cell layout is additive, as
/// in Potree: a point belongs to exactly one cell, the coarsest one whose sampling grid still
/// had room for it, so drawing a cell together with its ancestors gives an even sample of
/// that region at the depth the traversal stopped at, and the file never stores a point twice.
/// </remarks>
public sealed class PointTileStore : IDisposable
{
    public const string IndexFileName = "index.bin";
    public const string PointFileName = "points.bin";
    public const string AttributeFileName = "attributes.bin";

    private readonly MemoryMappedFile _points;
    private readonly MemoryMappedViewAccessor _pointView;
    private readonly MemoryMappedFile? _attributes;
    private readonly MemoryMappedViewAccessor? _attributeView;

    private PointTileStore(
        string directory,
        PointTileStoreHeader header,
        PointTileNode[] nodes,
        MemoryMappedFile points,
        MemoryMappedFile? attributes)
    {
        Directory = directory;
        Header = header;
        Nodes = nodes;
        _points = points;
        _pointView = points.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _attributes = attributes;
        _attributeView = attributes?.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public string Directory { get; }
    public PointTileStoreHeader Header { get; }

    /// <summary>The cell table, depth-first from the root. Small enough to stay resident.</summary>
    public PointTileNode[] Nodes { get; }

    public static PointTileStore Open(string directory)
    {
        byte[] index = File.ReadAllBytes(Path.Combine(directory, IndexFileName));
        PointTileStoreHeader header = PointTileStoreHeader.Read(index);

        var nodes = new PointTileNode[header.NodeCount];
        MemoryMarshal.Cast<byte, PointTileNode>(
                index.AsSpan(PointTileStoreHeader.Stride, header.NodeCount * PointTileNode.Stride))
            .CopyTo(nodes);

        MemoryMappedFile points = MemoryMappedFile.CreateFromFile(
            Path.Combine(directory, PointFileName), FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        string attributePath = Path.Combine(directory, AttributeFileName);
        MemoryMappedFile? attributes = header.HasAttributes && File.Exists(attributePath)
            ? MemoryMappedFile.CreateFromFile(attributePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read)
            : null;

        return new PointTileStore(directory, header, nodes, points, attributes);
    }

    /// <summary>Copies one cell's vertices out of the mapped point file.</summary>
    public void ReadPoints(in PointTileNode node, Span<GpuPointVertex> destination)
    {
        if (destination.Length < node.PointCount)
            throw new ArgumentException("Destination is smaller than the cell.", nameof(destination));

        ReadInto(_pointView, node.PointOffset * GpuPointVertex.Stride,
            MemoryMarshal.AsBytes(destination[..node.PointCount]));
    }

    /// <summary>Copies one cell's attributes, when the store carries them.</summary>
    public bool ReadAttributes(in PointTileNode node, Span<GpuPointAttribute> destination)
    {
        if (_attributeView is null)
            return false;
        if (destination.Length < node.PointCount)
            throw new ArgumentException("Destination is smaller than the cell.", nameof(destination));

        ReadInto(_attributeView, node.PointOffset * GpuPointAttribute.Stride,
            MemoryMarshal.AsBytes(destination[..node.PointCount]));
        return true;
    }

    private static unsafe void ReadInto(MemoryMappedViewAccessor view, long byteOffset, Span<byte> destination)
    {
        byte* source = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref source);
        try
        {
            new ReadOnlySpan<byte>(source + view.PointerOffset + byteOffset, destination.Length)
                .CopyTo(destination);
        }
        finally
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public void Dispose()
    {
        _attributeView?.Dispose();
        _attributes?.Dispose();
        _pointView.Dispose();
        _points.Dispose();
    }
}
