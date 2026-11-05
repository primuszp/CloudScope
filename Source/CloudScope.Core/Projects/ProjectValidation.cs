using CloudScope.Loading;
using CloudScope.Sections;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Projects;

internal static class ProjectValidation
{
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string field)
    { if (!ok) throw new InvalidDataException($"Invalid project {field}."); }
    internal static void Finite(Vector3 v) => Require(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), "coordinate");
    private static bool Name(string? s) => !string.IsNullOrWhiteSpace(s);
    public static void Validate(ProjectState s)
    {
        Require(s.Format == "CloudScope.Project" && s.Version == 1, "format/version");
        Require(s.Layers != null && s.Polylines != null && s.PlanarPolylines != null && s.Annotations != null
            && s.Definitions != null && s.NamedViews != null && s.Workspace != null, "collections");
        if (s.Source is { } source)
        {
            Require(source.Identity != null && source.PointCount >= 0 && source.PointCount == source.Identity.PointCount, "source identity");
            Require(source.Format is "LAS" or "PLY" or "XYZ" or "PTS" or "PTX" or "E57" or "Embedded", "source format");
            Require(source.Format == "Embedded" ? source.Path == "" : Name(source.Path), "source path");
            Require(float.IsFinite(source.Radius) && source.Radius >= 0 && float.IsFinite(source.ColorScale) && source.ColorScale > 0, "source scale");
            Require(double.IsFinite(source.Identity.OriginX) && double.IsFinite(source.Identity.OriginY) && double.IsFinite(source.Identity.OriginZ)
                && source.Identity.GeometrySha256 is { Length: 64 } && source.Identity.GeometrySha256.All(Uri.IsHexDigit) && source.PayloadSha256 is { Length: 64 } && source.PayloadSha256.All(Uri.IsHexDigit), "source digest");
        }
        Require(s.Source == null || s.Layers!.Length == 0, "mixed source/store state");
        Require(s.Layers!.All(l => l != null && l.Id != Guid.Empty && Name(l.Path) && Name(l.Name) && l.Identity is { Length: 64 })
            && s.Layers.Select(l => l.Id).Distinct().Count() == s.Layers.Length, "layers");
        foreach (var layer in s.Layers) { Finite(layer.Tint); Require(layer.Tint.X >= 0 && layer.Tint.Y >= 0 && layer.Tint.Z >= 0, "layer tint"); }
        var ids = new HashSet<int>();
        foreach (var line in s.Polylines!)
        {
            Require(line != null && ids.Add(line.Id) && line.Id > 0 && line.Id < int.MaxValue && Name(line.Name)
                && line.Vertices is { Length: >= 2 } && (!line.Closed || line.Vertices.Length >= 3), "3D polyline");
            foreach (var v in line!.Vertices) Finite(v);
        }
        foreach (var line in s.PlanarPolylines!)
        {
            Require(line != null && ids.Add(line.Id) && line.Id > 0 && line.Id < int.MaxValue && Name(line.Name)
                && line.Vertices is { Length: >= 2 } && (!line.Closed || line.Vertices.Length >= 3), "planar polyline");
            Finite(line!.Origin); Finite(line.AxisX); Finite(line.AxisY);
            Require(MathF.Abs(line.AxisX.LengthSquared - 1) < 0.001f && MathF.Abs(line.AxisY.LengthSquared - 1) < 0.001f
                && MathF.Abs(Vector3.Dot(line.AxisX, line.AxisY)) < 0.001f, "polyline plane");
            foreach (var v in line.Vertices) Require(float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y)
                && float.IsFinite(v.Bulge) && float.IsFinite(v.StartWidth) && v.StartWidth >= 0 && float.IsFinite(v.EndWidth) && v.EndWidth >= 0, "polyline vertex");
        }
        if (s.Section is { } section)
        {
            Finite(section.Start); Finite(section.End);
            Require(section.Id > 0 && section.Id < int.MaxValue && Name(section.Name) && section.Length > 0
                && float.IsFinite(section.Width) && section.Width > 0, "section");
        }
        Require(s.Definitions!.All(d => Name(d.Name)) && s.Definitions.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == s.Definitions.Length, "label definitions");
        foreach (var d in s.Definitions) { Finite(d.Color); Require(d.Color.X >= 0 && d.Color.X <= 1 && d.Color.Y >= 0 && d.Color.Y <= 1 && d.Color.Z >= 0 && d.Color.Z <= 1, "label color"); }
        var refs = new HashSet<(int,long)>();
        foreach (var a in s.Annotations!)
        {
            Require(a != null && Name(a.Name) && a.Instance is not < 0 && a.Point >= 0 && refs.Add((a.Layer,a.Point)), "annotation");
            Require(s.Source != null ? a!.Layer == -1 && a.Point < s.Source.PointCount
                : a!.Layer >= 0 && a.Layer < s.Layers.Length && a.Position.HasValue, "annotation source");
            if (a.Position is { } position) Finite(position);
        }
        Require(Enum.IsDefined(s.ColorSource) && double.IsFinite(s.KeepPercentage) && s.KeepPercentage >= 1 && s.KeepPercentage <= 100, "display filter");
        s.Filter?.Restore();
        Require(!s.HasSurface || s.Source != null, "surface source");
        var w = s.Workspace!;
        Require(w.Viewports is { Length: 9 } && w.ActiveViewport >= 0 && w.ActiveViewport < 9
            && w.Layout >= 0 && w.Layout <= 10 && w.PreviousLayout >= 0 && w.PreviousLayout <= 10
            && w.AuxiliaryView >= 0 && w.AuxiliaryView <= 6 && Name(w.CurrentLabel) && w.CurrentInstance is not < 0
            && Enum.IsDefined(w.Mode) && Enum.IsDefined(w.Tool), "workspace");
        foreach (var viewport in w.Viewports!)
        {
            Require(viewport != null && float.IsFinite(viewport.PointSize) && viewport.PointSize >= 0.5f && viewport.PointSize <= 20
                && Enum.IsDefined(viewport.SectionMode) && (s.Section.HasValue || viewport.SectionMode == SectionDisplayMode.None), "viewport");
            Camera(viewport!.Camera);
        }
        Require(s.NamedViews!.Keys.All(Name) && s.NamedViews.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == s.NamedViews.Count, "named views");
        foreach (var camera in s.NamedViews.Values) Camera(camera);
    }
    private static void Camera(OrbitCamera.CameraState c)
    {
        Finite(c.Translation); Finite(c.Pivot);
        Require(float.IsFinite(c.Azimuth) && float.IsFinite(c.Elevation) && double.IsFinite(c.HalfViewSize) && c.HalfViewSize >= 0.001
            && double.IsFinite(c.ViewAngle) && c.ViewAngle >= 0 && c.ViewAngle < 179, "camera");
    }
}
