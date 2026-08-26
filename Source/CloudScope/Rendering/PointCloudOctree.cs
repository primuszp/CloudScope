using System;
using System.Buffers;
using System.Collections.Generic;
using CloudScope.Loading;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>
/// One octree cell. Its points occupy a contiguous run of the uploaded buffer, so a cell
/// is drawn with a single range, and the run is ordered so that any prefix of it is an
/// even sample of the cell (see <see cref="PointCloudOctree"/>).
/// </summary>
internal readonly record struct PointOctreeNode(int Offset, int Count, Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
}

/// <summary>
/// Spatial layout for a resident point cloud: the order the points are uploaded in, plus
/// the octree cells that index into it.
/// </summary>
internal sealed class PointCloudOctreeLayout : IDisposable
{
    public static PointCloudOctreeLayout Empty { get; } = new(null, 0, Array.Empty<PointOctreeNode>());

    private int[]? _uploadOrder;

    public PointCloudOctreeLayout(int[]? uploadOrder, int count, PointOctreeNode[] nodes)
    {
        _uploadOrder = uploadOrder;
        Count = count;
        Nodes = nodes;
    }

    /// <summary>Point indices in upload order, or null when the cloud is empty.</summary>
    public int[]? UploadOrder => _uploadOrder;

    public int Count { get; }

    /// <summary>The leaf cells, in upload order.</summary>
    public PointOctreeNode[] Nodes { get; }

    public void Dispose()
    {
        int[]? uploadOrder = Interlocked.Exchange(ref _uploadOrder, null);
        if (uploadOrder is not null)
            ArrayPool<int>.Shared.Return(uploadOrder);
    }
}

/// <summary>
/// Builds the octree a large cloud is rendered through.
/// </summary>
/// <remarks>
/// Two properties make budgeted rendering work, and the renderers depend on both:
/// <list type="bullet">
/// <item>Every cell is one contiguous run of the buffer, so a cell costs one draw call and
/// can be frustum-culled on its own.</item>
/// <item>Inside a cell the points are shuffled with a fixed seed, so drawing the first N of
/// a cell yields an even sample of that cell instead of whatever order the file had. Without
/// it a reduced density shows the cloud in load order — dense where the scanner started,
/// empty elsewhere.</item>
/// </list>
/// </remarks>
internal static class PointCloudOctree
{
    /// <summary>Cells below this many points are not split further.</summary>
    private const int MinimumLeafSize = 32_768;

    /// <summary>Splitting stops here as well, to bound both build time and draw calls.</summary>
    private const int MaxDepth = 12;

    /// <summary>
    /// Cell count target. Cells are the unit of culling and of draw calls, so this trades
    /// culling precision against per-frame draw call count.
    /// </summary>
    private const int TargetLeafCount = 2_048;

    private const int ShuffleSeed = 0x5EED;

    /// <summary>Subtrees at least this large are built on their own thread.</summary>
    private const int ParallelSubtreeThreshold = 1_000_000;

    public static PointCloudOctreeLayout Build(PointCloudRenderData data, int pointCount)
    {
        if (pointCount <= 0)
            return PointCloudOctreeLayout.Empty;

        int[] order = ArrayPool<int>.Shared.Rent(pointCount);
        int[] scratch = ArrayPool<int>.Shared.Rent(pointCount);
        try
        {
            PointData[] points = data.Points;
            FillInitialOrder(data, pointCount, order);

            (Vector3 min, Vector3 max) = ComputeBounds(points, order, 0, pointCount);

            int leafSize = Math.Max(MinimumLeafSize, pointCount / TargetLeafCount);
            var nodes = new List<PointOctreeNode>(Math.Min(TargetLeafCount * 2, 8192));
            Subdivide(points, order, scratch, 0, pointCount, min, max, 0, leafSize, nodes);

            // Cells come out of the parallel build in subtree completion order; sorting by
            // offset restores upload order, which the renderers rely on for their ranges.
            PointOctreeNode[] ordered = nodes.ToArray();
            Array.Sort(ordered, static (left, right) => left.Offset.CompareTo(right.Offset));
            return new PointCloudOctreeLayout(order, pointCount, ordered);
        }
        catch
        {
            ArrayPool<int>.Shared.Return(order);
            throw;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(scratch);
        }
    }

    private static void Subdivide(
        PointData[] points,
        int[] order,
        int[] scratch,
        int offset,
        int count,
        Vector3 min,
        Vector3 max,
        int depth,
        int leafSize,
        List<PointOctreeNode> nodes)
    {
        if (count <= 0)
            return;

        if (count <= leafSize || depth >= MaxDepth)
        {
            nodes.Add(CreateLeaf(points, order, offset, count));
            return;
        }

        Vector3 center = (min + max) * 0.5f;

        // Counting sort of the range into the eight octants, using the scratch array so the
        // cells of this node come out contiguous and in a stable order.
        Span<int> counts = stackalloc int[8];
        for (int i = offset; i < offset + count; i++)
            counts[GetOctant(points[order[i]], center)]++;

        Span<int> starts = stackalloc int[8];
        Span<int> cursors = stackalloc int[8];
        int running = offset;
        for (int octant = 0; octant < 8; octant++)
        {
            starts[octant] = running;
            cursors[octant] = running;
            running += counts[octant];
        }

        for (int i = offset; i < offset + count; i++)
        {
            int index = order[i];
            scratch[cursors[GetOctant(points[index], center)]++] = index;
        }

        Array.Copy(scratch, offset, order, offset, count);

        // The eight subtrees own disjoint slices of both arrays, so a large node can hand
        // them to the thread pool. On a big cloud the split dominates load time.
        if (count >= ParallelSubtreeThreshold)
        {
            int[] octantStarts = starts.ToArray();
            int[] octantCounts = counts.ToArray();
            var subtreeNodes = new List<PointOctreeNode>[8];
            Parallel.For(0, 8, octant =>
            {
                subtreeNodes[octant] = new List<PointOctreeNode>();
                if (octantCounts[octant] == 0)
                    return;

                (Vector3 childMin, Vector3 childMax) = GetOctantBounds(min, max, center, octant);
                Subdivide(points, order, scratch, octantStarts[octant], octantCounts[octant],
                    childMin, childMax, depth + 1, leafSize, subtreeNodes[octant]);
            });

            for (int octant = 0; octant < 8; octant++)
                nodes.AddRange(subtreeNodes[octant]);
            return;
        }

        for (int octant = 0; octant < 8; octant++)
        {
            if (counts[octant] == 0)
                continue;

            (Vector3 childMin, Vector3 childMax) = GetOctantBounds(min, max, center, octant);
            Subdivide(points, order, scratch, starts[octant], counts[octant],
                childMin, childMax, depth + 1, leafSize, nodes);
        }
    }

    private static PointOctreeNode CreateLeaf(PointData[] points, int[] order, int offset, int count)
    {
        Shuffle(order, offset, count);
        (Vector3 min, Vector3 max) = ComputeBounds(points, order, offset, count);
        return new PointOctreeNode(offset, count, min, max);
    }

    /// <summary>
    /// Fisher-Yates over the cell, seeded from its offset so a rebuild of the same cloud
    /// produces the same picture.
    /// </summary>
    private static void Shuffle(int[] order, int offset, int count)
    {
        var random = new Random(ShuffleSeed ^ offset);
        for (int i = count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[offset + i], order[offset + j]) = (order[offset + j], order[offset + i]);
        }
    }

    private static (Vector3 Min, Vector3 Max) ComputeBounds(PointData[] points, int[] order, int offset, int count)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int i = offset; i < offset + count; i++)
        {
            PointData point = points[order[i]];
            var position = new Vector3(point.X, point.Y, point.Z);
            min = Vector3.ComponentMin(min, position);
            max = Vector3.ComponentMax(max, position);
        }

        return count > 0 ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    private static int GetOctant(PointData point, Vector3 center)
    {
        int octant = point.X >= center.X ? 1 : 0;
        if (point.Y >= center.Y) octant |= 2;
        if (point.Z >= center.Z) octant |= 4;
        return octant;
    }

    private static (Vector3 Min, Vector3 Max) GetOctantBounds(Vector3 min, Vector3 max, Vector3 center, int octant)
    {
        var childMin = new Vector3(
            (octant & 1) == 0 ? min.X : center.X,
            (octant & 2) == 0 ? min.Y : center.Y,
            (octant & 4) == 0 ? min.Z : center.Z);
        var childMax = new Vector3(
            (octant & 1) == 0 ? center.X : max.X,
            (octant & 2) == 0 ? center.Y : max.Y,
            (octant & 4) == 0 ? center.Z : max.Z);
        return (childMin, childMax);
    }

    /// <summary>
    /// Seeds the working order with the point indices to build over, in ascending order.
    /// </summary>
    /// <remarks>
    /// Ascending matters: every pass of the build reads the point array through this order, so
    /// a scattered one turns each read into a cache miss. Taking the whole cloud is therefore
    /// the identity. Only a cloud trimmed to the resident limit goes through
    /// <see cref="PointCloudRenderData.RenderOrder"/> — that picks an even subset of the whole
    /// cloud rather than its first however-many points — and the result is sorted straight back
    /// into ascending order. Which points is what the render order decides; the order they are
    /// visited in is ours, and the octree's own in-cell shuffle is what keeps the drawn sample
    /// even.
    /// </remarks>
    private static void FillInitialOrder(PointCloudRenderData data, int pointCount, int[] order)
    {
        if (data.RenderOrder is not { Count: > 0 } renderOrder || pointCount >= renderOrder.Count)
        {
            for (int i = 0; i < pointCount; i++)
                order[i] = i;
            return;
        }

        for (int i = 0; i < pointCount; i++)
            order[i] = renderOrder.Resolve(i);

        Array.Sort(order, 0, pointCount);
    }
}
