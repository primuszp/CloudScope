namespace CloudScope.Rendering;

/// <summary>The common physical allocation plan: every cell owns whole pages, even in a tiny multi-layer document.</summary>
internal static class PointTileWorkingSet
{
    internal static int Pages(IReadOnlyList<PointTileLayer> layers, long residentLimit, int pageSize)
    {
        if (pageSize <= 0 || residentLimit < pageSize) throw new ArgumentOutOfRangeException(nameof(residentLimit), "Streaming residency must allow at least one page.");
        long required = 0;
        foreach (var layer in layers) foreach (var node in layer.Store.Nodes)
            required += ((long)node.PointCount + pageSize - 1) / pageSize;
        return checked((int)Math.Max(1, Math.Min(required, residentLimit / pageSize)));
    }
}
