using CloudScope.Drawing;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>Bounded world-space line tessellation, shared by all viewports and GPU backends.</summary>
internal sealed class PolylineGeometryCache(long maximumBytes = 32L * 1024 * 1024)
{
    private readonly Dictionary<PlanarPolyline, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private long _frame, _use;
    public long CachedBytes { get; private set; }
    public int BuildCount { get; private set; }
    public void BeginFrame() => _frame++;
    public Geometry Get(PlanarPolyline line)
    {
        if (_entries.TryGetValue(line, out var cached))
        {
            if (cached.Frame == _frame || line.Vertices.AsSpan().SequenceEqual(cached.Vertices))
            {
                cached.Frame = _frame; cached.Use = ++_use; return cached.Value;
            }
            _entries.Remove(line); CachedBytes -= cached.Bytes;
        }
        BuildCount++;
        Geometry value;
        if (PlanarPolylineGeometry.HasWidth(line))
        {
            var (left, right) = PlanarPolylineGeometry.TessellateWidthEdges(line);
            value = new([], left, right);
        }
        else value = new(PlanarPolylineGeometry.Tessellate(line), [], []);
        long bytes = 256L + line.Vertices.Length * 20L + (value.Center.Length + value.Left.Length + value.Right.Length) * 12L;
        while (_entries.Count > 0 && CachedBytes + bytes > maximumBytes)
        {
            var oldest = _entries.MinBy(pair => pair.Value.Use);
            CachedBytes -= oldest.Value.Bytes; _entries.Remove(oldest.Key);
        }
        if (bytes <= maximumBytes)
        {
            _entries.Add(line, new(value, (PlanarPolylineVertex[])line.Vertices.Clone(), bytes, _frame, ++_use));
            CachedBytes += bytes;
        }
        return value;
    }
    public void Clear() { _entries.Clear(); CachedBytes = 0; }
    internal sealed record Geometry(Vector3[] Center, Vector3[] Left, Vector3[] Right)
    { public bool HasWidth => Left.Length > 0; }
    private sealed class Entry(Geometry value, PlanarPolylineVertex[] vertices, long bytes, long frame, long use)
    {
        public Geometry Value { get; } = value;
        public PlanarPolylineVertex[] Vertices { get; } = vertices;
        public long Bytes { get; } = bytes;
        public long Frame = frame, Use = use;
    }
}
