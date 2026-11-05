using System.Diagnostics;
using CloudScope.Library;
using CloudScope.Loading;

namespace CloudScope;

public sealed partial class ViewerController
{
    /// <summary>
    /// Work handed back from a background thread, run on the next frame. A cloud is read
    /// off the UI thread but uploaded on the one that owns the graphics context.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pendingWork = new();

    /// <summary>
    /// Output produced without a command being submitted — a load finishing. Shells route
    /// it to the command line, so a background result is reported where a typed one is.
    /// </summary>
    public event Action<string>? BackgroundMessage;

    private readonly BackgroundOperation<LoadedPointCloud> _loadOperation;
    private bool _disposed;

    /// <summary>Progress of the current load, or -1 after cancellation/publication.</summary>
    public int LoadProgress => _loadOperation.Progress;

    private void InvalidatePendingLoad() => _loadOperation.Cancel();

    /// <summary>
    /// Reads an uncompressed LAS file and shows it. The read happens on a background thread and
    /// the upload on the next frame, so the command returns at once and no shell has to
    /// own a loading path of its own to keep its window responsive.
    /// </summary>
    public string OpenPointCloud(string path, long maxPoints = 0)
        => BeginResidentLoad(path, maxPoints, "OPEN", "LAS", (file, limit, progress, cancellation) =>
        {
            using var reader = new LasReader(file);
            return PointCloudLoader.Load(reader, limit, progress, cancellation);
        });

    /// <summary>Loads PLY vertices through the same resident-cloud rendering path as LAS.</summary>
    public string OpenPlyPointCloud(string path, long maxPoints = 0)
        => BeginResidentLoad(path, maxPoints, "OPENPLY", "PLY", PlyPointCloudLoader.Load);

    /// <summary>Loads an XYZ text cloud through the resident-cloud rendering path.</summary>
    public string OpenXyzPointCloud(string path, long maxPoints = 0)
        => BeginResidentLoad(path, maxPoints, "OPENXYZ", "XYZ", XyzPointCloudLoader.Load);

    /// <summary>Loads PTS text points with optional intensity and RGB.</summary>
    public string OpenPtsPointCloud(string path, long maxPoints = 0)
        => BeginResidentLoad(path, maxPoints, "OPENPTS", "PTS", PtsPointCloudLoader.Load);

    public string OpenPtxPointCloud(string path, long maxPoints = 0)
        => BeginResidentLoad(path, maxPoints, "OPENPTX", "PTX", PtxPointCloudLoader.Load);

    public string OpenE57PointCloud(string path, long maxPoints = 0)
        => BeginResidentLoad(path, maxPoints, "OPENE57", "E57", E57PointCloudLoader.Load);

    private string BeginResidentLoad(string path, long maxPoints, string command,
        string format, Func<string, long, IProgress<int>?, CancellationToken, LoadedPointCloud> loader)
    {
        if (_disposed) return "The viewer is closed.";
        if (maxPoints < 0) return "Maximum point count must be zero (all) or positive.";
        if (string.IsNullOrWhiteSpace(path)) return $"{command} requires a file path.";
        if (!File.Exists(path)) return $"File not found: {path}";
        string name = Path.GetFileName(path);
        var timer = Stopwatch.StartNew();
        if (!_loadOperation.TryStart((cancellation, progress) => loader(path, maxPoints, progress, cancellation), cloud =>
        {
            LoadPointCloud(cloud.ToDataset());
            _selection.SetSourceFilePath(path, supportsLasExport: format == "LAS");
            _sourceName = name;
            _sourcePath = Path.GetFullPath(path);
            BackgroundMessage?.Invoke($"Loaded {cloud.LoadedCount:N0} {format} points from {name} ({timer.Elapsed.TotalSeconds:0.0}s).");
        }, error => BackgroundMessage?.Invoke($"{format} load failed: {error.Message}")))
            return "A point cloud is already loading.";
        return $"Reading {name}...";
    }
}
