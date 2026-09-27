using CloudScope.Commands;
using CloudScope.Loading;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    [CommandMethod("OPENE57", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Loads E57 numeric bitpack scans with pose transforms and checksum validation.", Syntax = "OPENE57 <path> [max points]")]
    public IEnumerable<PromptStep> OpenE57(CommandContext context)
    {
        var path = context.Editor.GetFileNameForOpen("Enter E57 file path:").Filtering("e57").Requiring();
        yield return path;
        if (!path.IsOk) yield break;
        var limit = context.Editor.GetInteger("Maximum point count <all>:").WithRange(1, int.MaxValue).WithDefault(0);
        yield return limit;
        if (!limit.IsOk) yield break;
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().OpenE57PointCloud(path.Value, limit.Value));
    }

    [CommandMethod("OPENPTX", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Loads PTX scans using their scanner transforms.", Syntax = "OPENPTX <path> [max points]")]
    public IEnumerable<PromptStep> OpenPtx(CommandContext context)
    {
        var path = context.Editor.GetFileNameForOpen("Enter PTX file path:").Filtering("ptx").Requiring();
        yield return path;
        if (!path.IsOk) yield break;
        var limit = context.Editor.GetInteger("Maximum point count <all>:").WithRange(1, int.MaxValue).WithDefault(0);
        yield return limit;
        if (!limit.IsOk) yield break;
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().OpenPtxPointCloud(path.Value, limit.Value));
    }

    private static readonly Keyword[] ExportKeywords =
    [new("PLYBINARY", "PlyBinary"), new("PLYASCII", "PlyAscii"), new("XYZ", "Xyz"), new("PTS", "Pts"), new("CSV", "Csv")];

    [CommandMethod("EXPORT", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File, Scope = CommandScope.Document,
        Summary = "Exports the filtered resident sample in its original coordinate frame.",
        Syntax = "EXPORT [PlyBinary/PlyAscii/Xyz/Pts/Csv] <path>")]
    public IEnumerable<PromptStep> Export(CommandContext context)
    {
        var format = context.Editor.GetKeywords("Export format [PlyBinary/PlyAscii/Xyz/Pts/Csv] <PlyBinary>:", ExportKeywords)
            .WithDefaultKeyword("PLYBINARY");
        yield return format;
        if (format.Status != PromptStatus.Keyword) yield break;
        var selected = Enum.Parse<PointCloudExportFormat>(format.Keyword, true);
        string extension = selected is PointCloudExportFormat.PlyBinary or PointCloudExportFormat.PlyAscii ? "ply" : format.Keyword.ToLowerInvariant();
        var path = context.Editor.GetFileNameForSave("Export file path:").Filtering(extension).Requiring();
        yield return path;
        if (!path.IsOk) yield break;
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().ExportPointCloud(path.Value, selected));
    }
}
