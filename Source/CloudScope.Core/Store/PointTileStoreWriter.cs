using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CloudScope.Store;

/// <summary>
/// Appends finished cells to a store on disk.
/// </summary>
/// <remarks>
/// Chunks are built on several threads and finish in no particular order, so appending is
/// serialized here: a chunk's cells are written as one block, which keeps the points of a
/// subtree together on disk and the node table's child indices contiguous.
/// </remarks>
internal sealed class PointTileStoreWriter : IDisposable
{
    private readonly string _directory;
    private readonly bool _hasAttributes;
    private readonly FileStream _points;
    private readonly FileStream? _attributes;
    private readonly List<PointTileNode> _nodes = [];
    private readonly List<int> _roots = [];
    private readonly Lock _gate = new();
    private long _pointOffset;

    public PointTileStoreWriter(string directory, bool hasAttributes)
    {
        _directory = directory;
        _hasAttributes = hasAttributes;
        _points = Create(PointTileStore.PointFileName);
        _attributes = hasAttributes ? Create(PointTileStore.AttributeFileName) : null;

        FileStream Create(string name) =>
            new(Path.Combine(directory, name), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
    }

    public int NodeCount => _nodes.Count;
    public int MaxLevel { get; private set; }

    /// <summary>
    /// Writes one chunk's cells. <paramref name="cells"/> holds a subtree whose children are
    /// indices into the same list; the first cell is its root.
    /// </summary>
    /// <remarks>
    /// The subtree is re-indexed breadth first on the way in. That is what makes a node's
    /// children contiguous in the table, so a node needs only a first index and a count, and
    /// it puts the coarse levels first in the point file - the order a traversal reads them.
    /// </remarks>
    public void Append(List<PointTileCell> cells)
    {
        if (cells.Count == 0)
            return;

        var breadthFirst = new List<int>(cells.Count) { 0 };
        var firstChild = new int[cells.Count];
        var childCount = new int[cells.Count];
        for (int i = 0; i < breadthFirst.Count; i++)
        {
            List<int> children = cells[breadthFirst[i]].Children;
            firstChild[i] = children.Count > 0 ? breadthFirst.Count : -1;
            childCount[i] = children.Count;
            breadthFirst.AddRange(children);
        }

        lock (_gate)
        {
            int baseIndex = _nodes.Count;
            for (int i = 0; i < breadthFirst.Count; i++)
            {
                PointTileCell cell = cells[breadthFirst[i]];
                long offset = AppendPoints(cell);
                MaxLevel = Math.Max(MaxLevel, cell.Level);
                _nodes.Add(new PointTileNode(
                    cell.Cube[0].Min, cell.Cube[1].Min, cell.Cube[2].Min,
                    cell.Cube[0].Max, cell.Cube[1].Max, cell.Cube[2].Max,
                    offset,
                    cell.PointCount,
                    firstChild[i] < 0 ? -1 : baseIndex + firstChild[i],
                    childCount[i],
                    cell.Level));
            }

            _roots.Add(baseIndex);
        }
    }

    private long AppendPoints(PointTileCell cell)
    {
        long offset = _pointOffset;
        if (cell.PointCount == 0)
            return offset;

        Span<StorePoint> points = cell.Points.AsSpan(0, cell.PointCount);
        var vertices = new GpuPointVertex[cell.PointCount];
        for (int i = 0; i < points.Length; i++)
            vertices[i] = points[i].Vertex;
        _points.Write(MemoryMarshal.AsBytes(vertices.AsSpan()));

        if (_attributes is not null)
        {
            var attributes = new GpuPointAttribute[cell.PointCount];
            for (int i = 0; i < points.Length; i++)
                attributes[i] = points[i].Attribute;
            _attributes.Write(MemoryMarshal.AsBytes(attributes.AsSpan()));
        }

        _pointOffset += cell.PointCount;
        return offset;
    }

    /// <summary>Closes the point files and writes the index.</summary>
    public void Complete(CloudBounds bounds, long sourcePointCount)
    {
        _points.Flush(true);
        _attributes?.Flush(true);

        var header = new PointTileStoreHeader(
            _pointOffset, _nodes.Count, MaxLevel, _hasAttributes,
            bounds.OriginX, bounds.OriginY, bounds.OriginZ,
            bounds.MinX, bounds.MinY, bounds.MinZ,
            bounds.MaxX, bounds.MaxY, bounds.MaxZ);

        var index = new byte[PointTileStoreHeader.Stride + _nodes.Count * PointTileNode.Stride];
        header.Write(index);
        MemoryMarshal.Cast<PointTileNode, byte>(CollectionsMarshal.AsSpan(_nodes))
            .CopyTo(index.AsSpan(PointTileStoreHeader.Stride));
        File.WriteAllBytes(Path.Combine(_directory, PointTileStore.IndexFileName), index);
    }

    public void Dispose()
    {
        _points.Dispose();
        _attributes?.Dispose();
    }
}
