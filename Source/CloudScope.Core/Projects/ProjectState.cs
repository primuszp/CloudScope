using System.Text.Json.Serialization;
using CloudScope.Drawing;
using CloudScope.Labeling;
using CloudScope.Loading;
using CloudScope.Sections;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Projects;

/// <summary>Portable document state. Source coordinates and point identities never depend on a camera.</summary>
public sealed record ProjectState
{
    [JsonRequired]
    public string Format { get; init; } = "CloudScope.Project";
    [JsonRequired]
    public int Version { get; init; } = 1;
    [JsonRequired]
    public ProjectSource? Source { get; init; }
    [JsonRequired]
    public ProjectLayer[] Layers { get; init; } = [];
    [JsonRequired]
    public Polyline3D[] Polylines { get; init; } = [];
    [JsonRequired]
    public PlanarPolyline[] PlanarPolylines { get; init; } = [];
    [JsonRequired]
    public SectionDefinition? Section { get; init; }
    [JsonRequired]
    public LabelDefinition[] Definitions { get; init; } = [];
    [JsonRequired]
    public ProjectAnnotation[] Annotations { get; init; } = [];
    [JsonRequired]
    public Dictionary<string, OrbitCamera.CameraState> NamedViews { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonRequired]
    public ProjectWorkspace Workspace { get; init; } = new();
    [JsonRequired]
    public ProjectFilter? Filter { get; init; }
    [JsonRequired]
    public ColorSource ColorSource { get; init; }
    [JsonRequired]
    public double KeepPercentage { get; init; } = 100;
    [JsonRequired]
    public bool HasSurface { get; init; }
}

public sealed record ProjectSource(string Path, string Format, int PointCount, LabelSourceIdentity Identity,
    float Radius, bool HasColor, float ColorScale, string PayloadSha256);
public sealed record ProjectLayer(Guid Id, string Path, string Name, bool Visible, Vector3 Tint, string Identity);
public sealed record ProjectAnnotation(int Layer, long Point, string Name, int? Instance, Vector3? Position = null);
public sealed record ProjectViewport(OrbitCamera.CameraState Camera, float PointSize, SectionDisplayMode SectionMode);
public sealed record ProjectWorkspace
{
    public ProjectViewport[] Viewports { get; init; } = [];
    public int Layout { get; init; }
    public int PreviousLayout { get; init; }
    public int AuxiliaryView { get; init; }
    public int ActiveViewport { get; init; }
    public bool ResidentVisible { get; init; } = true;
    public bool SurfaceVisible { get; init; } = true;
    public bool Ortho { get; init; }
    public InteractionMode Mode { get; init; }
    public SelectionToolType Tool { get; init; }
    public string CurrentLabel { get; init; } = "Ground";
    public int? CurrentInstance { get; init; }
    public bool ExplorerVisible { get; init; } = true;
    public bool PropertiesVisible { get; init; } = true;
    public bool RibbonVisible { get; init; } = true;
    public bool PalettesOnRight { get; init; }
    public bool CommandLineVisible { get; init; } = true;
    public bool CommandLineFloating { get; init; }
    public bool HistoryVisible { get; init; }
    public bool LabelsVisible { get; init; }
}

public sealed record ProjectFilter(string Kind, byte[] Values, double Min = 0, double Max = 0)
{
    public static ProjectFilter? Capture(PointFilter? filter) => filter switch
    {
        null => null,
        ClassFilter f => new("Class", f.Values), ReturnFilter f => new("Return", f.Values),
        IntensityFilter f => new("Intensity", [], f.Min, f.Max), ZFilter f => new("Z", [], f.Min, f.Max),
        _ => throw new InvalidDataException("This filter cannot be saved in a project.")
    };
    public PointFilter Restore() => Kind switch
    {
        "Class" when Values is { Length: > 0 } => new ClassFilter(Values),
        "Return" when Values is { Length: > 0 } => new ReturnFilter(Values),
        "Intensity" when Min >= 0 && Max <= ushort.MaxValue && Min <= Max && Min == Math.Truncate(Min) && Max == Math.Truncate(Max)
            => new IntensityFilter((ushort)Min, (ushort)Max),
        "Z" when double.IsFinite(Min) && double.IsFinite(Max) && Min <= Max => new ZFilter(Min, Max),
        _ => throw new InvalidDataException("Invalid project filter.")
    };
}
