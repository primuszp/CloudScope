using System;
using System.Collections.Generic;
using CloudScope.Store;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>One cell the traversal decided to draw this frame.</summary>
/// <param name="LayerIndex">Which layer's store the cell belongs to.</param>
/// <param name="NodeIndex">Index into that store's <see cref="PointTileStore.Nodes"/>.</param>
/// <param name="Depth">Near distance of the cell, for eviction and load priority.</param>
internal readonly record struct PointTileVisit(int LayerIndex, int NodeIndex, float Depth);

/// <summary>
/// Walks an on-disk cloud's cell table and picks the cells one frame should draw.
/// </summary>
/// <remarks>
/// This is <see cref="PointLodPlanner"/>'s rule — a cell asks for the points its own screen
/// footprint can show — turned inside out for the additive store. There, a cell already holds
/// a sample of its region, so the density question is answered by <em>how far down to
/// descend</em> rather than by how much of a resident buffer to draw: the traversal stops at
/// the cell whose points are about as dense as the pixels under it, and the ancestors already
/// visited supply the coarser part of the same sample. That is why frame cost tracks the
/// screen and not the file, and why the descent never touches the far side of a two-billion
/// point cloud at all.
/// </remarks>
internal static class PointTileTraversal
{
    /// <summary>
    /// Points per pixel a cell must fall below before its children are worth loading. Above
    /// one point per pixel the extra detail only overdraws itself.
    /// </summary>
    private const float PointsPerPixel = 1.0f;

    /// <summary>
    /// Finds the cells no other cell claims as a child — where a traversal starts.
    /// </summary>
    /// <remarks>
    /// The store's index does not name them: the builder writes one subtree per chunk, so a
    /// store is a forest whose roots sit at the chunk level rather than at level zero. They
    /// are recovered here instead of costing the format a section, since a single sweep of a
    /// table that is five megabytes at two billion points is cheaper than the compatibility
    /// break would be.
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

        int count = 0;
        for (int i = 0; i < claimed.Length; i++)
        {
            if (!claimed[i])
                count++;
        }

        var roots = new int[count];
        int next = 0;
        for (int i = 0; i < claimed.Length; i++)
        {
            if (!claimed[i])
                roots[next++] = i;
        }

        return roots;
    }

    /// <summary>
    /// Collects the visible cells, nearest first, until the frame's point budget runs out.
    /// </summary>
    /// <param name="layers">
    /// Every layer on screen. They are walked out of one heap and against one budget, so the
    /// frame's points go to whatever is nearest the camera rather than being split by a rule
    /// that does not know where anything is.
    /// </param>
    /// <param name="view">Camera for the frame, in the store's origin-relative space.</param>
    /// <param name="drawBudget">Points this frame may draw.</param>
    /// <param name="destination">Receives the chosen cells; also caps how many are returned.</param>
    /// <param name="scratch">Reused traversal queue, so a frame allocates nothing.</param>
    /// <returns>How many entries of <paramref name="destination"/> were filled.</returns>
    public static int Collect(
        IReadOnlyList<PointTileLayer> layers,
        in PointRenderView view,
        int drawBudget,
        Span<PointTileVisit> destination,
        PointTileTraversalScratch scratch,
        out long plannedPointCount)
    {
        plannedPointCount = 0;
        if (layers.Count == 0 || drawBudget <= 0 || destination.IsEmpty)
            return 0;

        Matrix4 viewProjection = view.View * view.Projection;
        float pointArea = MathF.Max(view.PointSize * view.PointSize, 1f);

        // Breadth-first by depth: a nearer cell is both more valuable to draw and more urgent
        // to load, so the budget and the load queue are spent on it first. The queue is a
        // binary heap keyed on the cell's near distance.
        PointTileHeap heap = scratch.Reset();
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            PointTileLayer layer = layers[layerIndex];
            if (!layer.Visible)
                continue;

            PointTileNode[] layerNodes = layer.Store.Nodes;
            foreach (int root in layer.Roots)
            {
                if (TryProject(layerNodes[root], ref viewProjection, view.ViewportWidth, view.ViewportHeight,
                        out float rootArea, out float rootDepth))
                    heap.Push(layerIndex, root, rootDepth, rootArea);
            }
        }

        int count = 0;
        while (count < destination.Length
            && heap.TryPop(out int layer, out int index, out float depth, out float screenArea))
        {
            PointTileNode[] nodes = layers[layer].Store.Nodes;
            PointTileNode node = nodes[index];
            if (node.PointCount > 0)
            {
                if (plannedPointCount + node.PointCount > drawBudget)
                    break;

                destination[count++] = new PointTileVisit(layer, index, depth);
                plannedPointCount += node.PointCount;
            }

            // Descend only where the screen can still show more than this cell holds. A cell
            // whose points already outnumber its pixels ends the descent for its whole
            // subtree, which is the entire reason the far side of the cloud is never read.
            bool wantsDetail = screenArea >= float.MaxValue
                || node.PointCount * pointArea < screenArea * PointsPerPixel;
            if (!wantsDetail)
                continue;

            for (int child = 0; child < node.ChildCount; child++)
            {
                int childIndex = node.FirstChild + child;
                if ((uint)childIndex >= (uint)nodes.Length)
                    continue;
                if (TryProject(nodes[childIndex], ref viewProjection, view.ViewportWidth, view.ViewportHeight,
                        out float childArea, out float childDepth))
                    heap.Push(layer, childIndex, childDepth, childArea);
            }
        }

        return count;
    }

    /// <summary>
    /// Projects a cell's bounds and reports the pixel area it covers plus a depth to order by,
    /// or false when the cell is entirely outside the frustum. A cell crossing the eye plane
    /// reports <see cref="float.MaxValue"/>, since its footprint is unbounded there.
    /// </summary>
    private static bool TryProject(
        in PointTileNode node,
        ref Matrix4 viewProjection,
        int viewportWidth,
        int viewportHeight,
        out float screenArea,
        out float depth)
    {
        screenArea = 0f;
        depth = 0f;

        bool outsideLeft = true, outsideRight = true;
        bool outsideBottom = true, outsideTop = true;
        bool outsideNear = true, outsideFar = true;
        bool crossesEyePlane = false;

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        float minW = float.PositiveInfinity;

        for (int corner = 0; corner < 8; corner++)
        {
            var position = new Vector4(
                (corner & 1) == 0 ? node.MinX : node.MaxX,
                (corner & 2) == 0 ? node.MinY : node.MaxY,
                (corner & 4) == 0 ? node.MinZ : node.MaxZ,
                1f);
            Vector4 clip = position * viewProjection;

            outsideLeft &= clip.X < -clip.W;
            outsideRight &= clip.X > clip.W;
            outsideBottom &= clip.Y < -clip.W;
            outsideTop &= clip.Y > clip.W;
            outsideNear &= clip.Z < -clip.W;
            outsideFar &= clip.Z > clip.W;

            if (clip.W <= 1e-6f)
            {
                crossesEyePlane = true;
                continue;
            }

            minW = MathF.Min(minW, clip.W);
            minX = MathF.Min(minX, clip.X / clip.W);
            maxX = MathF.Max(maxX, clip.X / clip.W);
            minY = MathF.Min(minY, clip.Y / clip.W);
            maxY = MathF.Max(maxY, clip.Y / clip.W);
        }

        if (outsideLeft || outsideRight || outsideBottom || outsideTop || outsideNear || outsideFar)
            return false;

        if (crossesEyePlane || minW == float.PositiveInfinity)
        {
            screenArea = float.MaxValue;
            return true;
        }

        // Clamped to the viewport: a cell hanging off the edge only has to fill the pixels it
        // actually reaches.
        float width = (MathF.Min(maxX, 1f) - MathF.Max(minX, -1f)) * 0.5f * viewportWidth;
        float height = (MathF.Min(maxY, 1f) - MathF.Max(minY, -1f)) * 0.5f * viewportHeight;
        screenArea = MathF.Max(width, 0f) * MathF.Max(height, 0f);
        depth = minW;
        return true;
    }
}
