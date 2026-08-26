using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Store;

/// <summary>
/// A point of an on-disk cloud, identified by where it sits in the point file.
/// </summary>
/// <remarks>
/// Sixty-four bits, because a cloud can hold more points than an <see cref="int"/> counts. The
/// index is a position in <c>points.bin</c>, which the builder writes once and never reorders,
/// so it identifies the same point for the life of the store — that is what lets a label
/// outlive the session that made it without holding any points in memory.
/// </remarks>
public readonly record struct PointRef(int LayerIndex, long PointIndex) : IComparable<PointRef>
{
    public int CompareTo(PointRef other)
    {
        int layer = LayerIndex.CompareTo(other.LayerIndex);
        return layer != 0 ? layer : PointIndex.CompareTo(other.PointIndex);
    }
}

/// <summary>
/// Resolves a selection volume against a cloud that lives on disk.
/// </summary>
/// <remarks>
/// The in-memory path tests every point in the cloud, which at two billion points is a minute
/// of pure memory traffic even before the array has to exist. Here the cell tree does the work
/// the array cannot: a cell whose bounds miss the volume is skipped along with its whole
/// subtree, so a selection reads the handful of cells that actually overlap it. The cells are
/// exactly the ones the renderer streams, so a volume the user can see is usually already warm
/// in the page cache.
/// </remarks>
public static class PointTileVolumeQuery
{
    /// <summary>
    /// Every point of <paramref name="store"/> inside <paramref name="query"/>.
    /// </summary>
    /// <param name="layerIndex">Which layer the returned references belong to.</param>
    /// <remarks>
    /// Points are returned in point-file order, which is also the order the cells are laid out
    /// in, so a caller that walks the result reads the file forwards.
    /// </remarks>
    public static List<PointRef> Resolve(
        PointTileStore store,
        IPointSelectionQuery query,
        int layerIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var hits = new List<PointRef>();
        if (query.IsEmpty)
            return hits;

        query.GetBounds(out Vector3 min, out Vector3 max);

        // The volume is in the space the points are drawn in, which is relative to the store's
        // own origin — the same space the cell bounds are stored in.
        var pending = new Stack<int>();
        foreach (int root in FindRoots(store.Nodes))
            pending.Push(root);

        GpuPointVertex[] buffer = ArrayPool<GpuPointVertex>.Shared.Rent(1 << 16);
        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int index = pending.Pop();
                PointTileNode node = store.Nodes[index];
                if (!Overlaps(node, min, max))
                    continue;

                // A cell holds its own sample and hands the rest down, so both the cell and
                // its children have to be visited: skipping the cell would drop the coarse
                // points of the region, skipping the children would drop the fine ones.
                for (int child = 0; child < node.ChildCount; child++)
                {
                    int childIndex = node.FirstChild + child;
                    if ((uint)childIndex < (uint)store.Nodes.Length)
                        pending.Push(childIndex);
                }

                if (node.PointCount <= 0)
                    continue;

                if (buffer.Length < node.PointCount)
                {
                    ArrayPool<GpuPointVertex>.Shared.Return(buffer);
                    buffer = ArrayPool<GpuPointVertex>.Shared.Rent(node.PointCount);
                }

                store.ReadPoints(node, buffer);
                for (int i = 0; i < node.PointCount; i++)
                {
                    GpuPointVertex point = buffer[i];
                    if (query.Contains(point.X, point.Y, point.Z))
                        hits.Add(new PointRef(layerIndex, node.PointOffset + i));
                }
            }
        }
        finally
        {
            ArrayPool<GpuPointVertex>.Shared.Return(buffer);
        }

        hits.Sort();
        return hits;
    }

    /// <summary>Reads back the positions of points named by <paramref name="refs"/>.</summary>
    /// <remarks>
    /// A label is stored as a reference, not a position, so anything that has to draw the
    /// labelled points asks for them here. The references are sorted, so this walks the point
    /// file forwards rather than seeking about in it.
    /// </remarks>
    public static Vector3[] ReadPositions(PointTileStore store, IReadOnlyList<PointRef> refs)
    {
        var positions = new Vector3[refs.Count];
        if (refs.Count == 0)
            return positions;

        GpuPointVertex[] buffer = ArrayPool<GpuPointVertex>.Shared.Rent(1);
        try
        {
            for (int i = 0; i < refs.Count; i++)
            {
                store.ReadPoints(refs[i].PointIndex, buffer.AsSpan(0, 1));
                positions[i] = new Vector3(buffer[0].X, buffer[0].Y, buffer[0].Z);
            }
        }
        finally
        {
            ArrayPool<GpuPointVertex>.Shared.Return(buffer);
        }

        return positions;
    }

    /// <summary>Whether a cell's bounds overlap the query box at all.</summary>
    private static bool Overlaps(in PointTileNode node, in Vector3 min, in Vector3 max) =>
        node.MaxX >= min.X && node.MinX <= max.X
        && node.MaxY >= min.Y && node.MinY <= max.Y
        && node.MaxZ >= min.Z && node.MinZ <= max.Z;

    /// <summary>
    /// The cells no other cell claims as a child — where a walk of the forest starts.
    /// </summary>
    /// <remarks>
    /// The builder writes one subtree per chunk, so a store is a forest whose roots sit at the
    /// chunk level rather than at level zero, and the index does not name them.
    /// </remarks>
    public static int[] FindRoots(ReadOnlySpan<PointTileNode> nodes)
    {
        var claimed = new bool[nodes.Length];
        foreach (PointTileNode node in nodes)
        {
            for (int child = 0; child < node.ChildCount; child++)
            {
                int index = node.FirstChild + child;
                if ((uint)index < (uint)claimed.Length)
                    claimed[index] = true;
            }
        }

        var roots = new List<int>();
        for (int i = 0; i < claimed.Length; i++)
        {
            if (!claimed[i])
                roots.Add(i);
        }

        return roots.ToArray();
    }
}
