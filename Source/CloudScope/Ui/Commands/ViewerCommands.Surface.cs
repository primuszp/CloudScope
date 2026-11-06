using CloudScope.Commands;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    [CommandMethod("TIN", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.Utility, Scope = CommandScope.Document,
        Summary = "Builds a terrain Delaunay TIN with lowest-per-cell sites and an explicit gap limit.",
        Syntax = "TIN [Ground/All] <cell size; 0 = exact XY> <maximum edge length>")]
    public IEnumerable<PromptStep> Terrain(CommandContext context)
    {
        var source = context.Editor.GetKeywords("TIN source [Ground/All] <Ground>:", new Keyword("GROUND", "Ground"), new Keyword("ALL", "All"))
            .WithDefaultKeyword("GROUND");
        yield return source; if (source.Status != PromptStatus.Keyword) yield break;
        var cell = context.Editor.GetDouble("TIN grid cell size; 0 keeps exact XY <1>:").WithRange(0, double.MaxValue).WithDefault(1);
        yield return cell; if (!cell.IsOk) yield break;
        var edge = context.Editor.GetDouble("Maximum triangle edge in plan <5>:").WithRange(.001, double.MaxValue).WithDefault(5);
        yield return edge; if (!edge.IsOk) yield break;
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().BuildTerrain(source.Is("GROUND"), new(cell.Value, edge.Value)));
    }

    [CommandMethod("RECONSTRUCT", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.Utility, Scope = CommandScope.Document,
        Summary = "Builds an approximate surface using PCA normals and greedy projection.",
        Syntax = "RECONSTRUCT <neighbors >= 3> <maximum edge length; 0 = auto>")]
    public IEnumerable<PromptStep> Reconstruct(CommandContext context)
    {
        var neighbors = context.Editor.GetInteger("Neighbors <15>:").WithRange(3, int.MaxValue).WithDefault(15);
        yield return neighbors;
        if (!neighbors.IsOk) yield break;
        var edge = context.Editor.GetDouble("Maximum edge length <0 = auto>:").WithRange(0, float.MaxValue).WithDefault(0);
        yield return edge;
        if (!edge.IsOk) yield break;
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().ReconstructSurface(neighbors.Value, edge.Single));
    }

    private static readonly Keyword[] SurfaceKeywords = [new("ON", "ON"), new("OFF", "OFf"), new("CLEAR", "CLear")];
    [CommandMethod("SURFACE", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Shows, hides or clears the surface; Clear also cancels reconstruction.", Syntax = "SURFACE [ON/OFf/CLear]")]
    public IEnumerable<PromptStep> Surface(CommandContext context)
    {
        var option = context.Editor.GetKeywords("Surface [ON/OFf/CLear] <ON>:", SurfaceKeywords).WithDefaultKeyword("ON");
        yield return option;
        if (option.Status != PromptStatus.Keyword) yield break;
        var viewer = context.GetTarget<ViewerController>();
        context.Editor.WriteMessage(option.Is("CLEAR") ? viewer.ClearSurface() : viewer.SetSurfaceVisibility(option.Is("ON")));
    }

    [CommandMethod("EXPORTOBJ", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File, Scope = CommandScope.Document,
        Summary = "Saves the reconstructed surface in the original coordinate frame.", Syntax = "EXPORTOBJ <path>")]
    public IEnumerable<PromptStep> ExportObj(CommandContext context)
    {
        var path = context.Editor.GetFileNameForSave("OBJ export path:").Filtering("obj").Requiring();
        yield return path;
        if (path.IsOk) context.Editor.WriteMessage(context.GetTarget<ViewerController>().ExportSurfaceObj(path.Value));
    }
}
