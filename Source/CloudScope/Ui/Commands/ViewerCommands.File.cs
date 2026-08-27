using CloudScope.Commands;
using CloudScope.Store;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] IndexOptionKeywords =
    [
        new("SOURCE", "Source"),
        new("CHUNK", "CHunk"),
        new("GRID", "Grid"),
        new("MINPOINTS", "MinPoints", "MP"),
        new("SCRATCH", "SCratch"),
        new("BUILD", "Build")
    ];

    [CommandMethod("OPEN", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Loads a LAS or LAZ point cloud into memory.",
        Syntax = "OPEN <path> [max points]")]
    public IEnumerable<PromptStep> Open(CommandContext context)
    {
        Editor ed = context.Editor;
        PromptFileStep path = ed.GetFileNameForOpen("Enter LAS file path:").Filtering("las").Requiring();
        yield return path;
        if (!path.IsOk) yield break;

        PromptIntegerStep limit = ed
            .GetInteger("Enter maximum point count <all>:")
            .WithRange(1, int.MaxValue)
            .WithDefault(0);
        yield return limit;

        long maxPoints = limit.IsOk ? limit.Value : 0;
        ed.WriteMessage(context.GetTarget<ViewerController>().OpenPointCloud(path.Value, maxPoints));
    }

    [CommandMethod("OPENSTORE", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Streams an indexed point tile store straight off disk.",
        Syntax = "OPENSTORE <store directory>")]
    public IEnumerable<PromptStep> OpenStore(CommandContext context)
    {
        PromptFileStep directory = context.Editor
            .GetDirectory("Enter point tile store directory:")
            .Requiring();
        yield return directory;
        if (!directory.IsOk) yield break;

        context.Editor.WriteMessage(context.GetTarget<ViewerController>().OpenPointTileStore(directory.Value));
    }

    [CommandMethod("ADDSTORE", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Adds another point tile store as a layer beside the open ones.",
        Syntax = "ADDSTORE <store directory>")]
    public IEnumerable<PromptStep> AddStore(CommandContext context)
    {
        PromptFileStep directory = context.Editor
            .GetDirectory("Enter point tile store directory to add as a layer:")
            .Requiring();
        yield return directory;
        if (!directory.IsOk) yield break;

        context.Editor.WriteMessage(
            context.GetTarget<ViewerController>().OpenPointTileStore(directory.Value, replace: false));
    }

    [CommandMethod("LAYER", "LA", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Lists layers, or turns one on, off or closed.",
        Syntax = "LAYER [List/ON/OFf/Close] <name>")]
    public IEnumerable<PromptStep> Layer(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStep option = ed
            .GetKeywords("Layer [List/ON/OFf/Close] <List>:", LayerKeywords)
            .WithDefaultKeyword("LIST");
        yield return option;

        if (option.Is("LIST") || option.Status != PromptStatus.Keyword)
        {
            ed.WriteMessage(DescribeLayers(viewer));
            yield break;
        }

        PromptStringStep name = ed.GetLine($"Enter layer name to turn {option.Keyword.ToLowerInvariant()}:");
        yield return name;
        if (!name.IsOk) yield break;

        ed.WriteMessage(option.Keyword switch
        {
            "ON" => viewer.SetLayerVisible(name.Value, visible: true),
            "OFF" => viewer.SetLayerVisible(name.Value, visible: false),
            _ => viewer.CloseLayer(name.Value)
        });
    }

    private static string DescribeLayers(ViewerController viewer)
    {
        if (viewer.Layers.Count == 0)
            return "No layers are open. OPENSTORE loads one, ADDSTORE adds another.";

        return string.Join(
            Environment.NewLine,
            viewer.Layers.Select(layer =>
                $"  {(layer.Visible ? "on " : "off")}  {layer.Name}  {layer.PointCount:N0} points"));
    }

    [CommandMethod("INDEX", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.File, Scope = CommandScope.Application,
        Summary = "Indexes a LAS file into a point tile store of any size.",
        Syntax = "INDEX <las> [directory] [Source] [CHunk n] [Grid n] [MinPoints n] [SCratch dir]")]
    public IEnumerable<PromptStep> Index(CommandContext context)
    {
        Editor ed = context.Editor;

        PromptFileStep source = ed.GetFileNameForOpen("Enter LAS file to index:").Filtering("las").Requiring();
        yield return source;
        if (!source.IsOk) yield break;

        // Defaulting the store to sit beside the file it came from is what makes INDEX a
        // one-argument command in practice.
        string defaultDirectory = Path.ChangeExtension(source.Value, null) + ".cctiles";
        PromptFileStep target = ed
            .GetDirectory($"Enter store directory <{Path.GetFileName(defaultDirectory)}>:")
            .WithKeywords(new Keyword("DEFAULT", "Default"))
            .WithDefaultKeyword("DEFAULT");
        yield return target;

        string directory = target.IsOk ? target.Value : defaultDirectory;
        PointTileBuildOptions options = PointTileBuildOptions.Default;
        bool withSource = false;

        // Build options are read as repeated keyword/value pairs so a script can set as many
        // as it needs and a person can set none.
        while (true)
        {
            PromptStep option = ed
                .GetKeywords("Index option [Source/CHunk/Grid/MinPoints/SCratch/Build] <Build>:", IndexOptionKeywords)
                .WithDefaultKeyword("BUILD");
            yield return option;

            if (option.Status != PromptStatus.Keyword || option.Is("BUILD"))
                break;

            if (option.Is("SOURCE"))
            {
                withSource = true;
                ed.WriteMessage("Source record numbers will be written.");
                continue;
            }

            if (option.Is("SCRATCH"))
            {
                PromptFileStep scratch = ed.GetDirectory("Enter scratch directory:");
                yield return scratch;
                if (scratch.IsOk) options = options with { ScratchDirectory = scratch.Value };
                continue;
            }

            PromptIntegerStep value = ed
                .GetInteger($"Enter value for {option.Keyword}:")
                .WithRange(1, int.MaxValue);
            yield return value;
            if (!value.IsOk) continue;

            options = option.Keyword switch
            {
                "CHUNK" => options with { ChunkTargetPoints = value.Value },
                "GRID" => options with { CellGridResolution = value.Value },
                _ => options with { MinimumCellPoints = value.Value }
            };
        }

        // The build streams the file three times and never holds more than one chunk, so its
        // cost is the disk's, not the machine's memory.
        PointTileBuildReport report = PointTileStoreBuilder.Build(
            source.Value, directory, options with { WriteSourceIndices = withSource });

        string note = withSource
            ? " It records where each point came from, so labels can be written back to the LAS."
            : " Add the Source option if labels will be written back to the LAS.";
        ed.WriteMessage(
            $"Indexed {report.SourcePoints:N0} points into {report.NodeCount:N0} cells "
            + $"({report.Total.TotalSeconds:0.0}s). Open it with OPENSTORE.{note}");
    }

    [CommandMethod("STOREINFO", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory,
        Group = CommandGroup.Inquiry, Scope = CommandScope.Viewer,
        Summary = "Reports the structure of the open point tile stores.",
        Syntax = "STOREINFO")]
    public IEnumerable<PromptStep> StoreInfo(CommandContext context)
    {
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().DescribeStores());
        yield break;
    }
}
