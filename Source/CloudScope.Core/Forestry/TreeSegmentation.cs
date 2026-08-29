using OpenTK.Mathematics;

namespace CloudScope.Forestry;

/// <summary>Parameters for seed-assisted segmentation of terrestrial/SLAM tree clouds.</summary>
public sealed record TreeSegmentationOptions
{
    public float VoxelSize { get; init; } = 0.10f;
    public float ConnectionRadius { get; init; } = 0.28f;
    public float MaximumCrownRadius { get; init; } = 12f;
    public float MaximumSeedDistance { get; init; } = 0.75f;
    public float CompetitorCellSize { get; init; } = 0.35f;
    public float MinimumTrunkSpan { get; init; } = 1.5f;
    public int MinimumTrunkPoints { get; init; } = 8;
}

public sealed record TreeSegmentationResult(
    IReadOnlyList<int> PointIndices,
    Vector3 SeedPoint,
    int CompetitorCount,
    string? FailureReason = null)
{
    public bool Succeeded => FailureReason is null && PointIndices.Count > 0;
}

/// <summary>
/// Seeded, multi-source 3D graph growth for terrestrial laser scans. Vertical point columns
/// become competing trunk markers; Dijkstra growth then assigns connected voxels to the closest
/// marker. This deliberately leaves disconnected vegetation out instead of swallowing a nearby tree.
/// </summary>
public static class TreeSegmentation
{
    public static TreeSegmentationResult Segment(
        IReadOnlyList<PointData> points, Vector3 requestedSeed, TreeSegmentationOptions? options = null,
        IReadOnlySet<int>? excludedPointIndices = null)
    {
        options ??= new TreeSegmentationOptions();
        if (points.Count == 0)
            return new([], requestedSeed, 0, "No resident point cloud is loaded.");

        int seedIndex = Nearest(points, requestedSeed, options.MaximumSeedDistance);
        if (seedIndex < 0)
            return new([], requestedSeed, 0, $"No point lies within {options.MaximumSeedDistance:0.##} m of the seed.");

        Vector3 seed = Position(points[seedIndex]);
        float maxR2 = options.MaximumCrownRadius * options.MaximumCrownRadius;
        var candidates = new List<int>();
        for (int i = 0; i < points.Count; i++)
        {
            if (excludedPointIndices?.Contains(i) == true && i != seedIndex) continue;
            float dx = points[i].X - seed.X;
            float dy = points[i].Y - seed.Y;
            if (dx * dx + dy * dy <= maxR2) candidates.Add(i);
        }

        float voxel = MathF.Max(options.VoxelSize, 0.02f);
        var cells = new Dictionary<Cell, List<int>>();
        foreach (int index in candidates)
        {
            Cell cell = Cell.Of(points[index], voxel);
            if (!cells.TryGetValue(cell, out List<int>? members)) cells[cell] = members = [];
            members.Add(index);
        }

        Cell ownCell = Cell.Of(points[seedIndex], voxel);
        if (!cells.ContainsKey(ownCell)) return new([], seed, 0, "The seed voxel is empty.");

        List<Cell> competitors = FindCompetingTrunks(points, candidates, seed, options)
            .Select(p => Cell.Of(p, voxel)).Where(c => c != ownCell && cells.ContainsKey(c)).Distinct().ToList();

        var owner = new Dictionary<Cell, int>();
        var distance = new Dictionary<Cell, float>();
        var queue = new PriorityQueue<(Cell cell, int owner), float>();
        AddSource(ownCell, 0);
        for (int i = 0; i < competitors.Count; i++) AddSource(competitors[i], i + 1);

        int reach = Math.Max(1, (int)MathF.Ceiling(options.ConnectionRadius / voxel));
        while (queue.TryDequeue(out var item, out float cost))
        {
            if (!distance.TryGetValue(item.cell, out float best) || cost > best + 1e-5f || owner[item.cell] != item.owner) continue;
            for (int x = -reach; x <= reach; x++) for (int y = -reach; y <= reach; y++) for (int z = -reach; z <= reach; z++)
            {
                if (x == 0 && y == 0 && z == 0) continue;
                float step = voxel * MathF.Sqrt(x * x + y * y + z * z);
                if (step > options.ConnectionRadius) continue;
                Cell next = new(item.cell.X + x, item.cell.Y + y, item.cell.Z + z);
                if (!cells.ContainsKey(next)) continue;
                float nextCost = cost + step * (z < 0 ? 1.15f : 1f);
                if (distance.TryGetValue(next, out float old) && old <= nextCost) continue;
                distance[next] = nextCost; owner[next] = item.owner; queue.Enqueue((next, item.owner), nextCost);
            }
        }

        var result = new List<int>();
        foreach ((Cell cell, List<int> members) in cells)
            if (owner.TryGetValue(cell, out int marker) && marker == 0) result.AddRange(members);
        result.Sort();
        return result.Count == 0
            ? new([], seed, competitors.Count, "The seed is not connected to a tree-sized point cluster.")
            : new(result, seed, competitors.Count);

        void AddSource(Cell cell, int marker)
        {
            owner[cell] = marker; distance[cell] = 0f; queue.Enqueue((cell, marker), 0f);
        }
    }

    private static IEnumerable<Vector3> FindCompetingTrunks(IReadOnlyList<PointData> points,
        IReadOnlyList<int> candidates, Vector3 seed, TreeSegmentationOptions o)
    {
        float size = MathF.Max(o.CompetitorCellSize, 0.1f);
        var columns = new Dictionary<(int x, int y), (float min, float max, int count, double sx, double sy, int nearest)>();
        foreach (int index in candidates)
        {
            PointData p = points[index];
            var key = ((int)MathF.Floor(p.X / size), (int)MathF.Floor(p.Y / size));
            if (!columns.TryGetValue(key, out var c)) c = (p.Z, p.Z, 0, 0, 0, index);
            float oldD = DistanceSquaredXY(points[c.nearest], seed);
            float newD = DistanceSquaredXY(p, seed);
            columns[key] = (MathF.Min(c.min, p.Z), MathF.Max(c.max, p.Z), c.count + 1,
                c.sx + p.X, c.sy + p.Y, newD < oldD ? index : c.nearest);
        }

        foreach (var c in columns.Values)
        {
            if (c.count < o.MinimumTrunkPoints || c.max - c.min < o.MinimumTrunkSpan) continue;
            var center = new Vector3((float)(c.sx / c.count), (float)(c.sy / c.count), points[c.nearest].Z);
            if (DistanceSquaredXY(center, seed) < size * size * 2.25f) continue;
            yield return Position(points[c.nearest]);
        }
    }

    private static int Nearest(IReadOnlyList<PointData> points, Vector3 target, float maximum)
    {
        float best = maximum * maximum; int found = -1;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = Position(points[i]) - target; float d2 = d.LengthSquared;
            if (d2 <= best) { best = d2; found = i; }
        }
        return found;
    }

    private static float DistanceSquaredXY(PointData p, Vector3 q) =>
        (p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y);
    private static float DistanceSquaredXY(Vector3 p, Vector3 q) =>
        (p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y);
    private static Vector3 Position(PointData p) => new(p.X, p.Y, p.Z);

    private readonly record struct Cell(int X, int Y, int Z)
    {
        public static Cell Of(PointData p, float s) => new((int)MathF.Floor(p.X / s), (int)MathF.Floor(p.Y / s), (int)MathF.Floor(p.Z / s));
        public static Cell Of(Vector3 p, float s) => new((int)MathF.Floor(p.X / s), (int)MathF.Floor(p.Y / s), (int)MathF.Floor(p.Z / s));
    }
}
