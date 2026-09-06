using CloudScope.Commands;
using CloudScope.Forestry;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] LabelDefKeywords =
    [
        new("LIST", "List"),
        new("DELETE", "DElete"),
        new("COLOR", "Color", "COLOUR")
    ];

    private static readonly Keyword LasKeyword = new("LAS", "Las");

    [CommandMethod("NAVIGATE", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Viewer,
        Summary = "Switches to navigation mode.",
        Syntax = "NAVIGATE")]
    public IEnumerable<PromptStep> Navigate(CommandContext context)
    {
        context.GetTarget<ViewerController>().SetMode(InteractionMode.Navigate);
        context.Editor.WriteMessage("Mode: Navigate");
        yield break;
    }

    [CommandMethod("LABELMODE", "L", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Toggles between label mode and navigation mode.",
        Syntax = "LABELMODE")]
    public IEnumerable<PromptStep> LabelMode(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        InteractionMode target = viewer.Mode == InteractionMode.Label
            ? InteractionMode.Navigate
            : InteractionMode.Label;

        viewer.SetMode(target);
        context.Editor.WriteMessage($"Mode: {target}");
        yield break;
    }

    [CommandMethod("LABEL", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Sets the label (and optionally the instance) new selections are given.",
        Syntax = "LABEL <name> [instance id]")]
    public IEnumerable<PromptStep> Label(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        while (true)
        {
            PromptStringStep name = ed.GetLine("Enter label name or <name> <instance id>:");
            yield return name;
            if (!name.IsOk) yield break;

            if (!TryParseLabelAnnotation(name.Text, out string label, out int? instanceId,
                    out bool instanceSpecified, out string error))
            {
                ed.WriteMessage(error);
                continue;
            }

            ed.WriteMessage(instanceSpecified
                ? viewer.SetActiveAnnotation(label, instanceId)
                : viewer.SetActiveLabel(label));
            yield break;
        }
    }

    [CommandMethod("INSTANCE", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Sets or clears the instance id new selections are given.",
        Syntax = "INSTANCE <id> | CLear")]
    public IEnumerable<PromptStep> Instance(CommandContext context)
    {
        PromptIntegerStep id = context.Editor
            .GetInteger("Enter instance id or [CLear]:")
            .WithRange(0, int.MaxValue)
            .WithKeywords(ClearKeyword);
        yield return id;

        var viewer = context.GetTarget<ViewerController>();
        if (id.Is("CLEAR"))
        {
            context.Editor.WriteMessage(viewer.SetActiveInstance(null));
            yield break;
        }

        if (!id.IsOk) yield break;
        context.Editor.WriteMessage(viewer.SetActiveInstance(id.Value));
    }

    [CommandMethod("TREESEG", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Segments one terrestrial/SLAM tree from a picked trunk seed.",
        Syntax = "TREESEG <seed point>")]
    public IEnumerable<PromptStep> SegmentTree(CommandContext context)
    {
        Editor ed = context.Editor;
        PromptPointStep seed = ed.GetPoint("Pick a point on the tree trunk:");
        yield return seed;
        if (!seed.IsOk) yield break;

        string message = context.GetTarget<ViewerController>().SegmentTree(seed.Value);
        ed.WriteMessage(message);
    }

    [CommandMethod("GROUNDSEG", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Separates terrain points with the CSF cloth-simulation ground filter.",
        Syntax = "GROUNDSEG [resolution] [rigidness] [timeStep] [threshold] [iterations] [ON/OFF]")]
    public IEnumerable<PromptStep> SegmentGround(CommandContext context)
    {
        Editor ed = context.Editor;
        var d = new GroundSegmentationOptions();

        // Each CSF parameter is its own prompt step: the runtime re-asks an out-of-range
        // answer, applies the default on Enter, and still runs the whole thing in one line
        // ("GROUNDSEG 0.5 3 0.65 0.5 500 On") by feeding the tokens to successive steps.
        PromptDoubleStep resolution = ed.GetDouble($"Cloth resolution <{d.ClothResolution:0.###}>:")
            .WithDefault(d.ClothResolution).WithRange(0.05, 1000);
        yield return resolution;
        if (resolution.IsCancelled) yield break;

        PromptIntegerStep rigidness = ed.GetInteger($"Rigidness, 1 (steep) to 15 (flat) <{d.Rigidness}>:")
            .WithDefault(d.Rigidness).WithRange(1, 15);
        yield return rigidness;
        if (rigidness.IsCancelled) yield break;

        PromptDoubleStep timeStep = ed.GetDouble($"Time step <{d.TimeStep:0.###}>:")
            .WithDefault(d.TimeStep).WithRange(0.05, 2);
        yield return timeStep;
        if (timeStep.IsCancelled) yield break;

        PromptDoubleStep threshold = ed.GetDouble($"Classification threshold <{d.ClassThreshold:0.###}>:")
            .WithDefault(d.ClassThreshold).WithRange(0.01, 100);
        yield return threshold;
        if (threshold.IsCancelled) yield break;

        PromptIntegerStep iterations = ed.GetInteger($"Maximum iterations <{d.Iterations}>:")
            .WithDefault(d.Iterations).WithRange(1, 5000);
        yield return iterations;
        if (iterations.IsCancelled) yield break;

        PromptKeywordStep slopeSmooth = ed
            .GetKeywords($"Smooth slopes [ON/OFF] <{(d.SlopeSmoothing ? "ON" : "OFF")}>:",
                new Keyword("ON", "ON"), new Keyword("OFF", "OFF"))
            .WithDefaultKeyword(d.SlopeSmoothing ? "ON" : "OFF");
        yield return slopeSmooth;
        if (slopeSmooth.IsCancelled) yield break;

        ed.WriteMessage(context.GetTarget<ViewerController>().SegmentGround(new GroundSegmentationOptions
        {
            ClothResolution = resolution.Single,
            Rigidness = rigidness.Value,
            TimeStep = timeStep.Value,
            ClassThreshold = threshold.Single,
            Iterations = iterations.Value,
            SlopeSmoothing = slopeSmooth.Is("ON"),
        }));
    }

    [CommandMethod("LABELDEF", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Viewer,
        Summary = "Defines, colours, lists or deletes a label and its LAS class code.",
        Syntax = "LABELDEF <name> <class 0-255> [Color r,g,b] | List | DElete <name>")]
    public IEnumerable<PromptStep> LabelDef(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStringStep name = ed
            .GetString("Enter label name or [List/DElete]:")
            .WithKeywords(LabelDefKeywords);
        yield return name;

        if (name.Is("LIST"))
        {
            ed.WriteMessage(viewer.DescribeLabelDefinitions());
            yield break;
        }

        if (name.Is("DELETE"))
        {
            PromptStringStep target = ed.GetLine("Enter label name to delete:");
            yield return target;
            if (!target.IsOk) yield break;

            ed.WriteMessage(viewer.RemoveLabelDefinition(target.Value));
            yield break;
        }

        if (!name.IsOk) yield break;

        PromptIntegerStep code = ed
            .GetInteger($"Enter class code for '{name.Value}' (0-255):")
            .WithRange(0, 255);
        yield return code;
        if (!code.IsOk) yield break;

        // Colour is optional: Enter takes the palette colour the label would get anyway.
        PromptStringStep colour = ed
            .GetString("Enter colour r,g,b (0-1) or [Palette] <Palette>:")
            .WithKeywords(new Keyword("PALETTE", "Palette"))
            .WithDefaultKeyword("PALETTE");
        yield return colour;

        Vector3? colorValue = null;
        if (colour.IsOk)
        {
            if (!PromptPointStep.TryParsePoint(colour.Value, out Vector3 rgb))
            {
                ed.WriteMessage($"Invalid colour: {colour.Value}. Use r,g,b between 0 and 1.");
                yield break;
            }

            colorValue = Vector3.Clamp(rgb, Vector3.Zero, Vector3.One);
        }

        ed.WriteMessage(viewer.DefineLabel(name.Value, (byte)code.Value, colorValue));
    }

    [CommandMethod("LABELS", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Label, Scope = CommandScope.Viewer,
        Summary = "Shows or hides the label registry window.",
        Syntax = "LABELS")]
    public IEnumerable<PromptStep> LabelsWindow(CommandContext context)
    {
        context.GetTarget<ViewerController>().ToggleLabelWindow();
        context.Editor.WriteMessage("Label registry window toggled.");
        yield break;
    }

    [CommandMethod("LABELSTAT", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory,
        Group = CommandGroup.Inquiry, Scope = CommandScope.Document,
        Summary = "Reports how many points carry each label.",
        Syntax = "LABELSTAT")]
    public IEnumerable<PromptStep> LabelStat(CommandContext context)
    {
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().DescribeLabelStatistics());
        yield break;
    }

    [CommandMethod("UNLABEL",
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Removes the labels of every point inside the active selection volume.",
        Syntax = "UNLABEL")]
    public IEnumerable<PromptStep> Unlabel(CommandContext context)
    {
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().RemoveSelectionLabels());
        yield break;
    }

    [CommandMethod("SAVELABELS", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.File, Scope = CommandScope.Document,
        Summary = "Writes labels to JSON, or class codes into a copy of the LAS.",
        Syntax = "SAVELABELS [Las] [path]")]
    public IEnumerable<PromptStep> SaveLabels(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStep target = ed
            .GetKeywords("Save labels as [Json/Las] <Json>:", new Keyword("JSON", "Json"), LasKeyword)
            .WithDefaultKeyword("JSON");
        yield return target;

        if (target.Is("LAS"))
        {
            ed.WriteMessage(viewer.SaveLabelsToLas());
            yield break;
        }

        PromptFileStep path = ed.GetFileNameForSave("Enter label file path or [Default] <Default>:")
            .Filtering("json")
            .WithKeywords(new Keyword("DEFAULT", "Default"))
            .WithDefaultKeyword("DEFAULT");
        yield return path;

        string? destination = path.IsOk ? path.Value : null;
        ed.WriteMessage(viewer.SaveLabels(destination)
            ? "Labels saved."
            : "No source file is associated with the viewer.");
    }

    [CommandMethod("LOADLABELS",
        Group = CommandGroup.File, Scope = CommandScope.Document,
        Summary = "Loads labels from a JSON file.",
        Syntax = "LOADLABELS [path]")]
    public IEnumerable<PromptStep> LoadLabels(CommandContext context)
    {
        PromptFileStep path = context.Editor
            .GetFileNameForOpen("Enter label file path or [Default] <Default>:")
            .Filtering("json")
            .WithKeywords(new Keyword("DEFAULT", "Default"))
            .WithDefaultKeyword("DEFAULT");
        yield return path;

        string? source = path.IsOk ? path.Value : null;
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().LoadLabels(source)
            ? "Labels loaded."
            : "Label file could not be loaded.");
    }

    [CommandMethod("CLEARLABELS",
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Removes every label from the cloud.",
        Syntax = "CLEARLABELS")]
    public IEnumerable<PromptStep> ClearLabels(CommandContext context)
    {
        context.GetTarget<ViewerController>().ClearLabels();
        context.Editor.WriteMessage("All labels cleared.");
        yield break;
    }
}
