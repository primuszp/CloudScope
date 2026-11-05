using CloudScope.Loading;
using CloudScope.Labeling;
using CloudScope.Projects;
using CloudScope.Rendering;
using CloudScope.Store;
using OpenTK.Mathematics;

namespace CloudScope;

/// <summary>Staged, fully validated sources. Disposing an abandoned load closes every mapped store.</summary>
internal sealed class PreparedProject(ProjectState state, PointCloudDataset? dataset, SurfaceMesh? surface) : IDisposable
{
    public ProjectState State { get; } = state;
    public PointCloudDataset? Dataset { get; } = dataset;
    public SurfaceMesh? Surface { get; } = surface;
    public List<PointTileLayer> Layers { get; } = [];
    public StreamedSelectionSource? Streamed { get; private set; }
    public Dictionary<int, PointAnnotation> Annotations { get; private set; } = [];
    public bool Adopted { get; set; }
    public void Dispose() { if (!Adopted) { foreach (var layer in Layers) layer.Store.Dispose(); Layers.Clear(); } }

    public static PreparedProject Read(string path, CancellationToken cancellation, IProgress<int> progress)
    {
        var archive = ProjectFile.Read(path, cancellation);
        var state = archive.State;
        PointCloudDataset? dataset = archive.Embedded;
        if (state.Source is { } source)
        {
            dataset ??= ResidentPointCloudReader.Load(source.Path, source.Format, source.PointCount, progress, cancellation).ToDataset();
            if (dataset.SourceIdentity != source.Identity) throw new InvalidDataException("The project's point cloud identity differs from its saved source. The current document was preserved.");
            if (dataset.SourcePayloadSha256 != source.PayloadSha256) throw new InvalidDataException("The project source RGB/attribute payload has changed. The current document was preserved.");
            dataset.ApplyFilter(state.Filter?.Restore(), recolor: false);
            if (state.KeepPercentage != 100) dataset.SetThinning(state.KeepPercentage);
            dataset.SetColorSource(state.ColorSource, recolor: false);
        }
        if (archive.Surface is { } mesh && dataset is { } cloud)
        {
            if (mesh.Vertices.Length != cloud.VisibleCount || mesh.OriginX != cloud.OriginX || mesh.OriginY != cloud.OriginY || mesh.OriginZ != cloud.OriginZ)
                throw new InvalidDataException("Project surface differs from its point cloud.");
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                var p = cloud.ViewPoints[i];
                if (mesh.Vertices[i] != new Vector3(p.X,p.Y,p.Z)) throw new InvalidDataException("Project surface geometry differs from its point cloud.");
            }
        }
        var prepared = new PreparedProject(state, dataset, archive.Surface);
        try
        {
            foreach (var spec in state.Layers)
            {
                cancellation.ThrowIfCancellationRequested();
                var store = PointTileStore.Open(spec.Path);
                var layer = new PointTileLayer(spec.Name, store) { Visible = spec.Visible, Tint = spec.Tint, ProjectId = spec.Id };
                prepared.Layers.Add(layer);
                if (layer.ProjectIdentity != spec.Identity) throw new InvalidDataException($"The layout of project layer '{spec.Name}' has changed.");
            }
            var point = new GpuPointVertex[1];
            foreach (var a in state.Annotations)
            {
                cancellation.ThrowIfCancellationRequested();
                if (a.Layer < 0) continue;
                var store = prepared.Layers[a.Layer].Store;
                if (a.Point >= store.Header.PointCount) throw new InvalidDataException("A project annotation lies outside its store.");
                store.ReadPoints(a.Point, point);
                if (new Vector3(point[0].X, point[0].Y, point[0].Z) != a.Position)
                    throw new InvalidDataException("A project annotation's point has changed. The current document was preserved.");
            }
            if (prepared.Layers.Count > 0)
            {
                prepared.Streamed = new StreamedSelectionSource(prepared.Layers);
                prepared.Annotations = prepared.Streamed.Restore(state.Annotations.Select(a =>
                    (new PointRef(a.Layer, a.Point), new PointAnnotation(a.Name, a.Instance))).ToArray());
            }
            else prepared.Annotations = state.Annotations.ToDictionary(a => checked((int)a.Point), a => new PointAnnotation(a.Name, a.Instance));
            cancellation.ThrowIfCancellationRequested();
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }
}
