using CloudScope.Commands;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    [CommandMethod("RECONSTRUCT", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.Utility, Scope = CommandScope.Document,
        Summary = "Builds an approximate surface using PCA normals and greedy projection.",
        Syntax = "RECONSTRUCT <neighbors 3..32> <maximum edge length; 0 = auto>")]
    public IEnumerable<PromptStep> Reconstruct(CommandContext context)
    {
        var neighbors = context.Editor.GetInteger("Neighbors <15>:").WithRange(3, 32).WithDefault(15);
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
