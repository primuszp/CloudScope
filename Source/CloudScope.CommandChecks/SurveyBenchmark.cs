using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CloudScope;
using CloudScope.IO;
using CloudScope.Library;
using CloudScope.Loading;
using CloudScope.Projects;
using CloudScope.Rendering;
using CloudScope.Selection;
using CloudScope.Store;
using OpenTK.Mathematics;

/// <summary>Opt-in real-survey measurements. Outputs are always separate from the source survey.</summary>
internal static class SurveyBenchmark
{
    internal sealed record Sample(string Name, double Milliseconds, long AllocatedBytes, long PeakWorkingSetBytes);
    internal sealed record Report(string Runtime, string OS, int LogicalProcessors, long Points, string Store, int? ResidentVisiblePoints,
        bool IndexedSelectionMatchesFullScan, int SelectedPoints, int[] LayerRootCounts, Sample[] Operations);

    public static Report Run(string input, string output)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        if (Directory.Exists(input) && (output == input || output.StartsWith(input + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new ArgumentException("Benchmark output must be outside the input store.");
        Directory.CreateDirectory(output);
        string storePath = Directory.Exists(input) ? input : Path.Combine(output, "store");
        var operations = new List<Sample>();
        long sourceCount; int? residentVisiblePoints = null;
        if (File.Exists(input))
        {
            using (var reader = new LasReader(input)) sourceCount = reader.PointCount;
            residentVisiblePoints = Resident(input, operations);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var report = Measure("index-full", () => PointTileStoreBuilder.Build(input, storePath,
                PointTileBuildOptions.Default, new Progress()), operations);
            if (report.StoredPoints != sourceCount) throw new InvalidDataException("The index lost source points.");
        }
        else sourceCount = -1;
        using var store = Measure("store-open", () => PointTileStore.Open(storePath), operations);
        if (sourceCount >= 0 && store.Header.PointCount != sourceCount) throw new InvalidDataException("The store differs from the indexed source count.");
        sourceCount = store.Header.PointCount;
        if (sourceCount == 0) throw new InvalidDataException("The survey is empty.");
        var roots = PointTileVolumeQuery.FindRoots(store.Nodes);
        var first = new GpuPointVertex[1]; store.ReadPoints(0, first);
        var h = store.Header;
        var half = new Vector3((float)(h.MaxX - h.MinX), (float)(h.MaxY - h.MinY), (float)(h.MaxZ - h.MinZ)) * .01f;
        half = Vector3.ComponentMax(half, new Vector3(.01f));
        var box = new BoxSelectionTool { Center = new(first[0].X, first[0].Y, first[0].Z), HalfExtents = half };
        var query = box.CreateQuery();
        var hits = Measure("indexed-volume-query", () => PointTileVolumeQuery.Resolve(store, query), operations);
        var reference = Measure("full-scan-reference-query", () => FullScan(store, query), operations);
        bool correct = hits.Select(p => p.PointIndex).SequenceEqual(reference);
        if (!correct) throw new InvalidDataException("Indexed selection differs from the independent full point-file scan.");
        var positions = Measure("selected-position-readback", () => PointTileVolumeQuery.ReadPositions(store, hits), operations);
        if (positions.Length != hits.Count || positions.Any(p => !query.Contains(p.X, p.Y, p.Z)))
            throw new InvalidDataException("Selection readback returned the wrong points.");
        Measure("traversal-300-camera-frames", () => Traversal(store), operations);
        var result = new Report(RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, Environment.ProcessorCount,
            sourceCount, storePath, residentVisiblePoints, correct, hits.Count, [roots.Length], operations.ToArray());
        AtomicFile.WriteText(Path.Combine(output, "survey-cpu.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }

    public static void ResidentOnly(string input, string output)
    {
        var samples = new List<Sample>();
        int visible = Resident(Path.GetFullPath(input), samples);
        Directory.CreateDirectory(output);
        string json = JsonSerializer.Serialize(new { runtime = RuntimeInformation.FrameworkDescription, source = Path.GetFileName(input), visiblePoints = visible, operations = samples }, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteText(Path.Combine(output, "survey-resident.json"), json); Console.WriteLine(json);
    }

    private static int Resident(string path, List<Sample> operations)
    {
        var data = Measure("resident-load-full", () => ResidentPointCloudReader.Load(path, "LAS", 0, null, default).ToDataset(), operations);
        Measure("resident-geometry-and-attribute-checksum", () =>
        { _ = data.SourceIdentity; _ = data.SourcePayloadSha256; return data.LoadedCount; }, operations);
        Measure("resident-shader-color-state-1000-switches", () =>
        { for (int i = 0; i < 1000; i++) data.SetColorSource((ColorSource)(i % 5), recolor: false); return data.VisibleCount; }, operations);
        var range = new ZFilter(data.Attributes.MinZ, data.Attributes.MinZ + (data.Attributes.MaxZ - data.Attributes.MinZ) * .5);
        Measure("resident-height-filter-half-range", () => data.ApplyFilter(range, recolor: false), operations);
        int expected = data.Attributes.Z.Take(data.LoadedCount).Count(z => z >= range.Min && z <= range.Max);
        if (data.VisibleCount != expected) throw new InvalidDataException("Filtered density differs from the independent height count.");
        // A separate stage keeps the full import cost distinct from view/map allocations.
        GC.KeepAlive(data);
        return data.VisibleCount;
    }

    private static List<long> FullScan(PointTileStore store, IPointSelectionQuery query)
    {
        var result = new List<long>(); var points = new GpuPointVertex[65536];
        for (long offset = 0; offset < store.Header.PointCount; offset += points.Length)
        {
            int count = (int)Math.Min(points.Length, store.Header.PointCount - offset);
            store.ReadPoints(offset, points.AsSpan(0, count));
            for (int i = 0; i < count; i++) if (query.Contains(points[i].X, points[i].Y, points[i].Z)) result.Add(offset + i);
        }
        return result;
    }

    internal static OrbitCamera Camera(PointTileStore store)
    {
        var h = store.Header;
        double radius = .5 * Math.Sqrt(Math.Pow(h.MaxX - h.MinX, 2) + Math.Pow(h.MaxY - h.MinY, 2) + Math.Pow(h.MaxZ - h.MinZ, 2));
        var camera = new OrbitCamera(); camera.SetViewportSize(1920, 1080); camera.FitToCloud((float)Math.Max(.01, radius));
        return camera;
    }
    private static int Traversal(PointTileStore store)
    {
        var layers = new[] { new PointTileLayer("Benchmark", store) };
        var camera = Camera(store); var scratch = new PointTileTraversalScratch(); var visits = new PointTileVisit[Math.Max(1, store.Nodes.Length)];
        int total = 0;
        for (int i = 0; i < 300; i++)
        {
            camera.OrbitBy(1, 0);
            var view = new PointRenderView(camera.GetViewMatrix(), camera.GetProjectionMatrix(), 1920, 1080, 2);
            total += PointTileTraversal.Collect(layers, view, 2_000_000, visits, scratch, out _);
        }
        return total;
    }
    private static T Measure<T>(string name, Func<T> work, List<Sample> samples)
    {
        Console.Error.WriteLine($"Measuring {name}...");
        using var monitor = new PeakMemory();
        long before = GC.GetTotalAllocatedBytes(precise: true); var timer = Stopwatch.StartNew();
        T value = work();
        double elapsed = timer.Elapsed.TotalMilliseconds;
        samples.Add(new(name, elapsed, GC.GetTotalAllocatedBytes(precise: true) - before, monitor.Peak));
        return value;
    }
    private sealed class PeakMemory : IDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _monitor;
        private long _peak;
        public long Peak { get { Sample(); return Interlocked.Read(ref _peak); } }
        public PeakMemory()
        {
            Sample(); _monitor = Task.Run(async () =>
            { try { while (true) { await Task.Delay(25, _cancel.Token); Sample(); } } catch (OperationCanceledException) { } });
        }
        private void Sample() { lock (_process) { _process.Refresh(); _peak = Math.Max(_peak, _process.WorkingSet64); } }
        public void Dispose() { _cancel.Cancel(); _monitor.GetAwaiter().GetResult(); _cancel.Dispose(); _process.Dispose(); }
    }
    private sealed class Progress : IProgress<PointTileBuildProgress>
    {
        private long _last;
        public void Report(PointTileBuildProgress value)
        {
            long now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(_last, now).TotalSeconds < 2 && value.Done != value.Total) return;
            _last = now; Console.Error.WriteLine($"{value.Stage}: {value.Done:N0}/{value.Total:N0}");
        }
    }
}
