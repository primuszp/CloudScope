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

    /// <summary>Percentage of a load in progress, or -1 when nothing is loading.</summary>
    private int _loadProgress = -1;
    private int _loadGeneration;
    private bool _disposed;
    public int LoadProgress
    {
        get => Volatile.Read(ref _loadProgress);
        private set => Volatile.Write(ref _loadProgress, value);
    }

    private void InvalidatePendingLoad()
    {
        Interlocked.Increment(ref _loadGeneration);
        LoadProgress = -1;
    }

    /// <summary>
    /// Reads an uncompressed LAS file and shows it. The read happens on a background thread and
    /// the upload on the next frame, so the command returns at once and no shell has to
    /// own a loading path of its own to keep its window responsive.
    /// </summary>
    public string OpenPointCloud(string path, long maxPoints = 0)
        => BeginResidentLoad(path, maxPoints, "OPEN", "LAS", (file, limit, progress) =>
        {
            using var reader = new LasReader(file);
            return PointCloudLoader.Load(reader, limit, progress);
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
        string format, Func<string, long, IProgress<int>?, LoadedPointCloud> loader)
    {
        if (_disposed) return "The viewer is closed.";
        if (maxPoints < 0) return "Maximum point count must be zero (all) or positive.";
        if (string.IsNullOrWhiteSpace(path)) return $"{command} requires a file path.";
        if (!File.Exists(path)) return $"File not found: {path}";
        if (LoadProgress >= 0) return "A point cloud is already loading.";

        int generation = Interlocked.Increment(ref _loadGeneration);
        LoadProgress = 0;
        string name = Path.GetFileName(path);
        _ = Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var progress = new InlineProgress<int>(percent =>
                {
                    if (generation == Volatile.Read(ref _loadGeneration)) LoadProgress = percent;
                });
                LoadedPointCloud cloud = loader(path, maxPoints, progress);
                PointCloudDataset dataset = cloud.ToDataset();
                sw.Stop();
                _pendingWork.Enqueue(() =>
                {
                    if (_disposed || generation != _loadGeneration) return;
                    LoadPointCloud(dataset);
                    _selection.SetSourceFilePath(path, supportsLasExport: format == "LAS");
                    _sourceName = name;
                    _sourcePath = Path.GetFullPath(path);
                    LoadProgress = -1;
                    BackgroundMessage?.Invoke($"Loaded {cloud.LoadedCount:N0} {format} points from {name} ({sw.Elapsed.TotalSeconds:0.0}s).");
                });
            }
            catch (Exception ex)
            {
                _pendingWork.Enqueue(() =>
                {
                    if (_disposed || generation != _loadGeneration) return;
                    LoadProgress = -1;
                    BackgroundMessage?.Invoke($"{format} load failed: {ex.Message}");
                });
            }
        });
        return $"Reading {name}...";
    }

}
