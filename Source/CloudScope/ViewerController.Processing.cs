using CloudScope.Forestry;
using CloudScope.Labeling;
using CloudScope.Library.Enums;
using CloudScope.Store;
using OpenTK.Mathematics;

namespace CloudScope;

public sealed partial class ViewerController
{
    public string BuildTerrain(bool groundOnly, CloudScope.Terrain.TerrainOptions options)
    {
        if (_document.Dataset is not { } dataset) return "TIN requires a resident point cloud.";
        if (_surfaceOperation.IsRunning) return "A surface operation is already running.";
        var points = new ArraySegment<PointData>(dataset.SourcePoints,0,dataset.LoadedCount);
        var classes = dataset.Attributes.Class;
        var annotatedGround = _selection.Labels.AllAnnotations.Where(p =>
            string.Equals(p.Value.LabelName,"Ground",StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToHashSet();
        _surfaceOperation.TryStart((cancellation,progress) => CloudScope.Terrain.TerrainTriangulation.Build(points,options,
            (dataset.OriginX,dataset.OriginY,dataset.OriginZ),cancellation,progress,
            groundOnly ? i => classes[i] == (byte)ClassificationType.Ground || annotatedGround.Contains(i) : null), mesh =>
        {
            _surfaceRenderer.Upload(mesh); _document.SetSurface(mesh); _surfaceVisible = true;
            BackgroundMessage?.Invoke($"Terrain TIN: {mesh.Vertices.Length:N0} sites, {mesh.Indices.Length/3:N0} triangles. EXPORTOBJ saves the mesh.");
        },error => BackgroundMessage?.Invoke($"TIN failed: {error.Message}"));
        return "Building terrain TIN...";
    }

    private readonly ViewerProcessingService _processing;
    public int ProcessingProgress => _processing.Progress;
    internal Task ProcessingCompletion => _processing.Completion;

    public string SegmentTree(Vector3 seed, int? instanceId = null)
    {
        if (_disposed) return "The viewer is closed.";
        if (_document.Dataset == null || _document.Layers.Count > 0) return "Tree segmentation requires a resident point cloud.";
        var points = new ArraySegment<PointData>(_document.Dataset.SourcePoints, 0, _document.Dataset.LoadedCount);
        int id = instanceId ?? NextTreeInstanceId();
        var labels = _selection.Labels.AllAnnotations.ToArray();
        var classes = _document.Dataset.Attributes.Class;
        if (!_processing.Start("Tree segmentation", (cancellation, progress) =>
        {
            var ground = new HashSet<int>();
            for (int i = 0; i < points.Count; i++)
            {
                if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                if (classes[i] == (byte)ClassificationType.Ground) ground.Add(i);
            }
            foreach (var pair in labels)
                if (string.Equals(pair.Value.LabelName, "Ground", StringComparison.OrdinalIgnoreCase)) ground.Add(pair.Key);
            return TreeSegmentation.Segment(points, seed, excludedPointIndices: ground, cancellation: cancellation, progress: progress);
        }, result =>
        {
            if (result.Succeeded)
                UndoHistory.RunSeparate("TREESEG", () => _selection.Labels.ApplyAnnotation(result.PointIndices, new PointAnnotation("Tree", id)));
            BackgroundMessage?.Invoke(result.Succeeded ? $"Tree {id}: {result.PointIndices.Count:N0} points segmented; {result.CompetitorCount} competing trunk marker(s)."
                : $"Tree segmentation failed: {result.FailureReason}");
        })) return "Another processing operation is already running.";
        return "Segmenting tree...";
    }

    public string SegmentGround(GroundSegmentationOptions? options = null)
    {
        if (_disposed) return "The viewer is closed.";
        if (_document.Dataset == null || _document.Layers.Count > 0) return "Ground segmentation requires a resident point cloud.";
        var points = new ArraySegment<PointData>(_document.Dataset.SourcePoints, 0, _document.Dataset.LoadedCount);
        if (!_processing.Start("Ground segmentation", (cancellation, progress) => GroundSegmentation.Segment(points, options, cancellation, progress), result =>
        {
            if (result.Succeeded)
                UndoHistory.RunSeparate("GROUNDSEG", () => _selection.Labels.ApplyAnnotation(result.PointIndices, new PointAnnotation("Ground", null)));
            BackgroundMessage?.Invoke(result.Succeeded ? $"CSF ground: {result.PointIndices.Count:N0} ground and {result.NonGroundPointCount:N0} non-ground points; {result.ContactNodeCount:N0} / {result.ClothNodeCount:N0} cloth nodes contacted terrain."
                : $"Ground segmentation failed: {result.FailureReason}");
        })) return "Another processing operation is already running.";
        return "Segmenting ground...";
    }

    public string IndexPointCloud(string source, string directory, PointTileBuildOptions options)
    {
        if (_disposed) return "The viewer is closed.";
        if (!_processing.Start("Indexing", (cancellation, progress) => PointTileStoreBuilder.Build(source, directory, options,
            new IndexProgress(progress), cancellation), report => BackgroundMessage?.Invoke(
                $"Indexed {report.SourcePoints:N0} points into {report.NodeCount:N0} cells ({report.Total.TotalSeconds:0.0}s). Open it with OPENSTORE."), requiresDocument: false))
            return "Another processing operation is already running.";
        return "Indexing point cloud...";
    }

    internal bool CancelBackgroundOperations()
    {
        bool running = _projectOperation.IsRunning || _processing.IsRunning || _loadOperation.IsRunning || _surfaceOperation.IsRunning;
        _projectOperation.Cancel();
        _processing.Cancel();
        _loadOperation.Cancel();
        _surfaceOperation.Cancel();
        return running;
    }

    private sealed class IndexProgress(IProgress<int> progress) : IProgress<PointTileBuildProgress>
    {
        public void Report(PointTileBuildProgress value) => progress.Report(
            (int)value.Stage * 25 + (int)(25.0 * value.Done / Math.Max(1, value.Total)));
    }
}
