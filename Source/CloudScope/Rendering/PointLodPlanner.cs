using System;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>A contiguous run of the uploaded point buffer to draw.</summary>
internal readonly record struct PointDrawRange(int First, int Count);

/// <summary>
/// Picks, for one frame, how many points of each octree cell to draw.
/// </summary>
/// <remarks>
/// The cost of a point cloud frame is the point count, so the planner spends a fixed budget
/// where it buys the most: cells outside the frustum cost nothing, every visible cell gets a
/// floor so nothing pops out of existence, and the rest of the budget goes to the nearest
/// cells first, each capped at the density its own footprint on screen can actually show.
/// A cell twice as far away covers a quarter of the pixels and so asks for a quarter of the
/// points — that is what makes a hundred-million-point cloud draw at a constant cost instead
/// of a cost that grows with the file.
/// </remarks>
internal static class PointLodPlanner
{
    /// <summary>Points drawn for a visible cell before the remaining budget is shared out.</summary>
    private const int FloorPointsPerCell = 256;

    /// <summary>
    /// Points per pixel of a cell's screen footprint. Above one point per pixel the extra
    /// points only overdraw each other.
    /// </summary>
    private const float PointsPerPixel = 1.0f;

    public static int Plan(
        ReadOnlySpan<PointOctreeNode> nodes,
        in PointRenderView view,
        int drawBudget,
        Span<PointDrawRange> destination,
        PointLodScratch scratch,
        out int plannedPointCount)
    {
        plannedPointCount = 0;
        if (drawBudget <= 0 || nodes.IsEmpty || destination.IsEmpty)
            return 0;

        Matrix4 viewProjection = view.View * view.Projection;
        float pointArea = MathF.Max(view.PointSize * view.PointSize, 1f);

        Span<float> depths = scratch.Depths;
        Span<long> cells = scratch.Cells;
        Span<int> granted = scratch.Granted;

        int visibleCount = 0;
        int limit = Math.Min(nodes.Length, Math.Min(destination.Length, depths.Length));
        for (int i = 0; i < limit; i++)
        {
            PointOctreeNode node = nodes[i];
            if (node.Count <= 0)
                continue;

            if (!TryProject(node, ref viewProjection, view.ViewportWidth, view.ViewportHeight,
                    out float screenArea, out float depth))
                continue;

            int demand = screenArea >= float.MaxValue
                ? node.Count
                : (int)MathF.Min(node.Count, MathF.Ceiling(screenArea * PointsPerPixel / pointArea));

            depths[visibleCount] = depth;
            cells[visibleCount] = PackCell(i, Math.Max(demand, 1));
            visibleCount++;
        }

        if (visibleCount == 0)
            return 0;

        // Nearest first, so the budget runs out on the far cells rather than the near ones.
        // Cell index and demand travel as one packed payload, so this is a single key-value
        // sort with no comparison delegate: at a couple of thousand cells per frame that is
        // the difference between microseconds and milliseconds.
        depths[..visibleCount].Sort(cells[..visibleCount]);

        int remaining = drawBudget;
        for (int i = 0; i < visibleCount; i++)
        {
            (int nodeIndex, int demand) = UnpackCell(cells[i]);
            int floor = Math.Min(Math.Min(FloorPointsPerCell, nodes[nodeIndex].Count), demand);
            granted[i] = Math.Min(floor, Math.Max(remaining, 0));
            remaining -= granted[i];
        }

        for (int i = 0; i < visibleCount && remaining > 0; i++)
        {
            (_, int demand) = UnpackCell(cells[i]);
            int extra = Math.Min(demand - granted[i], remaining);
            if (extra <= 0)
                continue;

            granted[i] += extra;
            remaining -= extra;
        }

        int rangeCount = 0;
        for (int i = 0; i < visibleCount; i++)
        {
            if (granted[i] <= 0)
                continue;

            (int nodeIndex, _) = UnpackCell(cells[i]);
            destination[rangeCount++] = new PointDrawRange(nodes[nodeIndex].Offset, granted[i]);
            plannedPointCount += granted[i];
        }

        return rangeCount;
    }

    private static long PackCell(int nodeIndex, int demand) => ((long)nodeIndex << 32) | (uint)demand;

    private static (int NodeIndex, int Demand) UnpackCell(long cell) => ((int)(cell >> 32), (int)cell);

    /// <summary>
    /// Projects the cell's bounds and reports the pixel area it covers, plus a depth to sort
    /// by. Returns false when the cell is fully outside the frustum. A cell crossing the eye
    /// plane reports <see cref="float.MaxValue"/>, since its footprint is unbounded there.
    /// </summary>
    private static bool TryProject(
        PointOctreeNode node,
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
                (corner & 1) == 0 ? node.Min.X : node.Max.X,
                (corner & 2) == 0 ? node.Min.Y : node.Max.Y,
                (corner & 4) == 0 ? node.Min.Z : node.Max.Z,
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
            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            minX = MathF.Min(minX, ndcX);
            maxX = MathF.Max(maxX, ndcX);
            minY = MathF.Min(minY, ndcY);
            maxY = MathF.Max(maxY, ndcY);
        }

        if (outsideLeft || outsideRight || outsideBottom || outsideTop || outsideNear || outsideFar)
            return false;

        if (crossesEyePlane || minW == float.PositiveInfinity)
        {
            screenArea = float.MaxValue;
            depth = 0f;
            return true;
        }

        // The bounds are clamped to the viewport: a cell hanging off the edge only has to
        // fill the pixels it actually reaches.
        float width = (MathF.Min(maxX, 1f) - MathF.Max(minX, -1f)) * 0.5f * viewportWidth;
        float height = (MathF.Min(maxY, 1f) - MathF.Max(minY, -1f)) * 0.5f * viewportHeight;
        screenArea = MathF.Max(width, 0f) * MathF.Max(height, 0f);
        depth = minW;
        return true;
    }
}

/// <summary>
/// Per-frame working set for <see cref="PointLodPlanner"/>. The renderer keeps one of these
/// for the loaded cloud so that planning a frame allocates nothing.
/// </summary>
internal sealed class PointLodScratch
{
    public PointLodScratch(int cellCount)
    {
        Depths = new float[cellCount];
        Cells = new long[cellCount];
        Granted = new int[cellCount];
    }

    /// <summary>Sort keys: the near distance of each visible cell.</summary>
    public float[] Depths { get; }

    /// <summary>Cell index in the high half, point demand in the low half.</summary>
    public long[] Cells { get; }

    /// <summary>Points granted to each cell once the budget has been shared out.</summary>
    public int[] Granted { get; }
}
