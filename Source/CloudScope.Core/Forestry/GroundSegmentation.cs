namespace CloudScope.Forestry;

/// <summary>Settings for progressive raster ground extraction from terrestrial point clouds.</summary>
public sealed record GroundSegmentationOptions
{
    public float CellSize { get; init; } = 0.25f;
    public float PointHeightTolerance { get; init; } = 0.20f;
    public float SurfaceTolerance { get; init; } = 0.12f;
    public float MaximumSlopeDegrees { get; init; } = 45f;
    public float MaximumWindowRadius { get; init; } = 2f;
    public int MinimumSupportingScales { get; init; } = 2;
}

public sealed record GroundSegmentationResult(
    IReadOnlyList<int> PointIndices, int CellCount, int GroundCellCount, string? FailureReason = null)
{
    public bool Succeeded => FailureReason is null && PointIndices.Count > 0;
}

/// <summary>
/// Progressive multi-scale minimum filter. A raster minimum is retained when neighbouring
/// minima support it at several window sizes under the configured slope constraint; points
/// close to the retained surface are classified as ground.
/// </summary>
public static class GroundSegmentation
{
    public static GroundSegmentationResult Segment(
        IReadOnlyList<PointData> points, GroundSegmentationOptions? options = null)
    {
        options ??= new GroundSegmentationOptions();
        if (points.Count == 0) return new([], 0, 0, "No resident point cloud is loaded.");

        float cellSize = MathF.Max(options.CellSize, 0.05f);
        var cells = new Dictionary<Cell, CellData>();
        for (int i = 0; i < points.Count; i++)
        {
            Cell key = Cell.Of(points[i], cellSize);
            if (!cells.TryGetValue(key, out CellData? data)) cells[key] = data = new CellData();
            data.Indices.Add(i);
            if (points[i].Z < data.MinimumZ) data.MinimumZ = points[i].Z;
        }

        int maxRadius = Math.Max(1, (int)MathF.Ceiling(options.MaximumWindowRadius / cellSize));
        int[] radii = ProgressiveRadii(maxRadius);
        float slope = MathF.Tan(Math.Clamp(options.MaximumSlopeDegrees, 0f, 89f) * MathF.PI / 180f);
        var groundCells = new HashSet<Cell>();

        foreach ((Cell key, CellData data) in cells)
        {
            int support = 0;
            foreach (int radius in radii)
            {
                float bestEnvelope = float.PositiveInfinity;
                bool hasNeighbour = false;
                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (!cells.TryGetValue(new Cell(key.X + dx, key.Y + dy), out CellData? neighbour)) continue;
                    float distance = cellSize * MathF.Sqrt(dx * dx + dy * dy);
                    bestEnvelope = MathF.Min(bestEnvelope, neighbour.MinimumZ + slope * distance);
                    hasNeighbour = true;
                }
                if (hasNeighbour && data.MinimumZ <= bestEnvelope + options.SurfaceTolerance) support++;
            }

            if (support >= Math.Min(options.MinimumSupportingScales, radii.Length)) groundCells.Add(key);
        }

        var result = new List<int>();
        foreach (Cell key in groundCells)
        {
            CellData data = cells[key];
            float ceiling = data.MinimumZ + MathF.Max(options.PointHeightTolerance, 0.01f);
            foreach (int index in data.Indices)
                if (points[index].Z <= ceiling) result.Add(index);
        }
        result.Sort();
        return result.Count == 0
            ? new([], cells.Count, groundCells.Count, "No continuous ground surface met the configured slope and height tolerances.")
            : new(result, cells.Count, groundCells.Count);
    }

    private static int[] ProgressiveRadii(int maximum)
    {
        var result = new List<int>();
        for (int radius = 1; radius < maximum; radius *= 2) result.Add(radius);
        if (result.Count == 0 || result[^1] != maximum) result.Add(maximum);
        return result.ToArray();
    }

    private readonly record struct Cell(int X, int Y)
    {
        public static Cell Of(PointData p, float size) =>
            new((int)MathF.Floor(p.X / size), (int)MathF.Floor(p.Y / size));
    }

    private sealed class CellData
    {
        public float MinimumZ = float.PositiveInfinity;
        public List<int> Indices { get; } = [];
    }
}
