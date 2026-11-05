using System.Diagnostics;
using System.Text.Json;
using CloudScope;
using CloudScope.Commands;
using CloudScope.Drawing;
using CloudScope.Rendering;
using OpenTK.Mathematics;

internal static class PerformanceChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        int value = 0, notices = 0;
        var undo = new UndoManager(memoryBudgetBytes: 1024, maximumCommands: 10);
        undo.HistoryTrimmed += () => notices++;
        for (int i = 1; i <= 3; i++)
        {
            int before = value, after = i; value = after;
            undo.Record(new DelegateUndoAction("Edit", () => value = before, () => value = after, 512));
        }
        check("Undo history evicts old groups within its memory budget", undo.Depth == 2 && undo.EstimatedRetainedBytes == 1024 && notices == 1, "");
        check("Retained undo and redo remain coherent after eviction", undo.Undo(3) == 2 && value == 1 && undo.Redo(2) == 2 && value == 3, "");
        var labels = new CloudScope.Labeling.LabelManager(trackLocalHistory: false);
        labels.ActionRecorded += undo.Record;
        labels.ApplyLabel([0,1,2], "Tree");
        check("Viewer label changes use only the common bounded history", !labels.Undo(), "");
        int freed = 0;
        using (var pool = new FrameResourcePool<byte[]>(n => new byte[n], a => a.Length, _ => freed++, maximumCachedBytes: 64))
        {
            pool.BeginFrame(); for (int i = 0; i < 10; i++) pool.Rent(16);
            pool.Submit(1, () => true, () => { }, () => { });
            pool.BeginFrame();
            check("Completed GPU resource caches honor their byte limit", pool.CachedBytes == 64 && freed == 6, "");
        }
        check("Bounded GPU cache releases each resource once", freed == 10, "");
        var line = Fixture(50);
        var cache = new PolylineGeometryCache();
        for (int frame = 0; frame < 10; frame++)
        {
            cache.BeginFrame(); for (int view = 0; view < 9; view++) cache.Get(line);
        }
        check("Nine viewports share one tessellation across unchanged frames", cache.BuildCount == 1, "");
        line.Vertices[1] = line.Vertices[1] with { Position = new Vector2(2, 3) };
        cache.BeginFrame(); var rebuilt = cache.Get(line);
        var expected = PlanarPolylineGeometry.TessellateWidthEdges(line);
        check("In-place vertex edits invalidate cached geometry exactly", cache.BuildCount == 2 && rebuilt.Left.SequenceEqual(expected.Left) && rebuilt.Right.SequenceEqual(expected.Right), "");
        var bounded = new PolylineGeometryCache(maximumBytes: 1024);
        bounded.BeginFrame(); bounded.Get(Fixture(500));
        check("Oversized line geometry cannot fill the persistent cache", bounded.CachedBytes == 0, "");
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 20; frame++)
        {
            cache.BeginFrame(); for (int view = 0; view < 9; view++) cache.Get(line);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        check("Warm multi-viewport geometry lookup avoids repeated allocations", allocated < 1024, allocated.ToString());
    }

    public static void Benchmark()
    {
        var line = Fixture(500);
        var cache = new PolylineGeometryCache();
        cache.BeginFrame(); cache.Get(line);
        PlanarPolylineGeometry.TessellateWidthEdges(line);
        var baseline = Measure(() => { for (int i = 0; i < 270; i++) PlanarPolylineGeometry.TessellateWidthEdges(line); });
        var cached = Measure(() =>
        {
            for (int frame = 0; frame < 30; frame++)
            { cache.BeginFrame(); for (int view = 0; view < 9; view++) cache.Get(line); }
        });
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "500-vertex arc/width polyline, 30 frames, 9 viewports", baseline, cached,
            geometryBuilds = cache.BuildCount, cacheBytes = cache.CachedBytes }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static object Measure(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew(); action();
        return new { milliseconds = timer.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before };
    }
    private static PlanarPolyline Fixture(int count) => new(1, "Benchmark", Vector3.Zero, Vector3.UnitX, Vector3.UnitY,
        Enumerable.Range(0, count).Select(i => new PlanarPolylineVertex(new Vector2(i, i % 3), 0.2f, 0.2f, 0.3f)).ToArray());
}
