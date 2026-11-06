namespace CloudScope.Store;

/// <summary>Validates file dimensions and the additive forest before any unsafe mapped read or traversal.</summary>
internal static class PointTileValidation
{
    internal static void Validate(string directory, PointTileStoreHeader header, PointTileNode[] nodes)
    {
        static void Require(bool ok) { if (!ok) throw new InvalidDataException("Invalid point tile store structure."); }
        Require(header.PointCount > 0 && header.NodeCount == nodes.Length && nodes.Length > 0 && header.MaxLevel is >= 0 and <= 255);
        Require(new[] { header.OriginX, header.OriginY, header.OriginZ, header.MinX, header.MinY, header.MinZ, header.MaxX, header.MaxY, header.MaxZ }.All(double.IsFinite)
            && header.MinX <= header.MaxX && header.MinY <= header.MaxY && header.MinZ <= header.MaxZ);
        void FileLength(string name, int stride)
        {
            Require(header.PointCount <= long.MaxValue / stride);
            Require(File.Exists(Path.Combine(directory, name)) && new FileInfo(Path.Combine(directory, name)).Length == header.PointCount * stride);
        }
        FileLength(PointTileStore.PointFileName, GpuPointVertex.Stride);
        if (header.HasAttributes) FileLength(PointTileStore.AttributeFileName, GpuPointAttribute.Stride);
        if (header.HasSourceIndices) FileLength(PointTileStore.SourceIndexFileName, sizeof(long));
        var parents = new bool[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            Require(float.IsFinite(n.MinX) && float.IsFinite(n.MinY) && float.IsFinite(n.MinZ)
                && float.IsFinite(n.MaxX) && float.IsFinite(n.MaxY) && float.IsFinite(n.MaxZ)
                && n.MinX <= n.MaxX && n.MinY <= n.MaxY && n.MinZ <= n.MaxZ && n.Level <= header.MaxLevel);
            Require(n.PointCount >= 0 && n.PointOffset >= 0 && n.PointOffset <= header.PointCount && n.PointCount <= header.PointCount - n.PointOffset);
            Require(n.ChildCount >= 0 && (n.ChildCount == 0 || n.FirstChild >= 0 && n.FirstChild <= nodes.Length && n.ChildCount <= nodes.Length - n.FirstChild));
            for (int child = n.FirstChild; child < n.FirstChild + n.ChildCount; child++)
            {
                Require(child != i && !parents[child] && nodes[child].Level > n.Level);
                parents[child] = true;
            }
        }
        // Strictly increasing levels already prohibit cycles; one parent prohibits shared
        // subtrees. The sorted ranges must partition the point file without gaps/overlap.
        long end = 0;
        foreach (var node in nodes.Where(n => n.PointCount > 0).OrderBy(n => n.PointOffset))
        { Require(node.PointOffset == end); end += node.PointCount; }
        Require(end == header.PointCount && parents.Any(parent => !parent));
    }
}
