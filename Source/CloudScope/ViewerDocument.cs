using CloudScope.IO;
using CloudScope.Library;
using CloudScope.Loading;
using CloudScope.Rendering;

namespace CloudScope;

/// <summary>Owns source identity and resident/streamed document data, independently of any window.</summary>
internal sealed class ViewerDocument
{
    public PointCloudDataset? Dataset { get; private set; }
    public SurfaceMesh? Surface { get; private set; }
    public List<PointTileLayer> Layers { get; } = [];
    public string SourceName { get; private set; } = "";
    public string SourcePath { get; private set; } = "";
    public string SourceFormat { get; private set; } = "Embedded";

    public void ReplaceResident(PointCloudDataset? dataset)
    {
        Dataset = dataset;
        Surface = null;
        SourceName = SourcePath = "";
        SourceFormat = "Embedded";
    }
    public void SetSource(string path, string format = "LAS")
    {
        SourceFormat = format;
        SourceName = Path.GetFileName(path);
        SourcePath = path.Length == 0 ? "" : Path.GetFullPath(path);
    }
    public void SetDisplayName(string name) => SourceName = name;
    public void SetSurface(SurfaceMesh? surface) => Surface = surface;

    public string Export(string path, PointCloudExportFormat format, Func<int, byte?> annotationCode)
    {
        if (Dataset is not { } data) return "Export requires a resident point cloud.";
        if (FilePathIdentity.SamePath(path, SourcePath)) return "Choose an export path different from the source file.";
        AtomicFile.Write(path, stream => PointCloudExporter.Write(stream, data, format,
            index => annotationCode(index) ?? data.Attributes.Class[index]));
        return $"Exported {data.VisibleCount:N0} points to {path}.";
    }
}
