using CloudScope.Commands;
using CloudScope.IO;
using CloudScope.Loading;
using CloudScope.Measurements;
using CloudScope.Rendering;
using CloudScope.Sections;
using CloudScope.Store;
using OpenTK.Mathematics;

namespace CloudScope;

public sealed partial class ViewerController
{
    public IReadOnlyList<Measurement> Measurements => _document.Measurements.Items;
    public IReadOnlyList<SectionDefinition> Sections => _document.Sections.Items;

    public string AddMeasurement(string name, MeasurementKind kind, Vector3[] points)
    {
        var measurement = new Measurement(_document.Measurements.NextId,name,kind,points.ToArray());
        measurement.Evaluate();
        ReplaceMeasurements([..Measurements, measurement]);
        return Describe(measurement);
    }

    public string RemoveMeasurement(string name)
    {
        var next = Measurements.Where(m => !string.Equals(m.Name,name,StringComparison.OrdinalIgnoreCase)).ToArray();
        if (next.Length == Measurements.Count) return "Measurement not found.";
        ReplaceMeasurements(next); return "Measurement removed.";
    }

    private void ReplaceMeasurements(Measurement[] next)
    {
        var before = Measurements.ToArray();
        _document.Measurements.Replace(next);
        UndoHistory.Record(new DelegateUndoAction("Measurement", () => _document.Measurements.Replace(before),
            () => _document.Measurements.Replace(next)));
    }

    public string MeasurementDescription => Measurements.Count == 0 ? "No measurements."
        : string.Join(Environment.NewLine,Measurements.Select(Describe));

    private static string Describe(Measurement m)
    {
        var r = m.Evaluate();
        return m.Kind == MeasurementKind.Area
            ? FormattableString.Invariant($"{m.Name}: area XY {r.Area:0.######}, plan perimeter {r.Horizontal:0.######} (survey units).")
            : FormattableString.Invariant($"{m.Name}: distance 3D {r.Spatial:0.######}, horizontal {r.Horizontal:0.######}, delta Z {r.DeltaZ:0.######}, slope {(r.SlopePercent is { } s ? s.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture) + "%" : "undefined")}, angle {r.AngleDegrees:0.######} degrees (survey units).");
    }

    private (double X, double Y, double Z) DocumentOrigin => _document.Dataset is { } d
        ? (d.OriginX,d.OriginY,d.OriginZ)
        : _document.Layers.Count > 0 ? _document.Layers[0].Store.Header.Origin : (0,0,0);

    private void ValidateEngineeringExportPath(string path)
    {
        if (FilePathIdentity.SamePath(path,_document.SourcePath) || FilePathIdentity.SamePath(path,ProjectPath ?? "")
            || _document.Layers.Any(l => Path.GetFullPath(path).StartsWith(Path.GetFullPath(l.Store.Directory) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an export path outside the project and source data.");
    }

    public string ExportMeasurements(string path, EngineeringExportFormat format)
    {
        ValidateEngineeringExportPath(path);
        EngineeringExport.Measurements(path,format,Measurements,DocumentOrigin);
        return $"Exported {Measurements.Count} measurements to {path}.";
    }

    public string ActivateCrossSection(string name)
    {
        var before = CaptureSectionState();
        if (!_document.Sections.Activate(name)) return "Section not found.";
        SetCrossSectionState(_document.Sections.Active); ApplySectionCameras();
        RecordSectionChange(before,CaptureSectionState()); return CrossSectionDescription;
    }

    public string RenameCrossSection(string name)
    {
        if (_crossSection is not { } active) return "No active section.";
        var before = CaptureSectionState();
        SetCrossSectionState(active with { Name = name });
        RecordSectionChange(before,CaptureSectionState()); return CrossSectionDescription;
    }

    public string ExportCrossSection(string path, EngineeringExportFormat format)
    {
        if (_crossSection is not { } section) return "No active section.";
        ValidateEngineeringExportPath(path);
        var data = _document.Dataset;
        var layers = _document.Layers.Where(l => l.Visible).ToArray();
        var origin = DocumentOrigin;
        return _processing.Start("Section export", (cancellation, progress) =>
            EngineeringExport.Section(path,format,section,ReadSectionPoints(section,data,layers,origin,cancellation),cancellation),
            count => BackgroundMessage?.Invoke($"Exported {count:N0} section points to {path}."))
            ? "Exporting section points in the background." : "Another processing operation is running.";
    }

    private static IEnumerable<SectionPoint> ReadSectionPoints(SectionDefinition section, CloudScope.Loading.PointCloudDataset? data,
        PointTileLayer[] layers, (double X,double Y,double Z) origin, CancellationToken cancellation)
    {
        var query = new SectionPointQuery(section);
        if (data != null)
        {
            for (int i = 0; i < data.LoadedCount; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                int index = i;
                var p = data.SourcePoints[index];
                if (query.Contains(p.X,p.Y,p.Z)) yield return new(-1,index,new(p.X,p.Y,p.Z),p.X + origin.X,p.Y + origin.Y,p.Z + origin.Z);
            }
        }
        else
        {
            for (int layer = 0; layer < layers.Length; layer++)
            {
                var store = layers[layer].Store;
                var hits = PointTileVolumeQuery.Resolve(store,query,layer,cancellation);
                // Bounded reads keep export memory proportional to hits, independent of renderer residency.
                for (int first = 0; first < hits.Count; first += 65536)
                {
                    var batch = hits.GetRange(first,Math.Min(65536,hits.Count - first));
                    var positions = PointTileVolumeQuery.ReadPositions(store,batch,cancellation);
                    for (int i = 0; i < positions.Length; i++)
                    {
                        var p = positions[i]; var o = store.Header.Origin;
                        yield return new(layer,batch[i].PointIndex,p,p.X + o.X,p.Y + o.Y,p.Z + o.Z);
                    }
                }
            }
        }
    }
}
