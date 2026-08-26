using CloudScope.Commands;
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

    [CommandMethod("NAVIGATE", "NAV", "N", Flags = CommandFlags.NoUndoMarker,
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

    [CommandMethod("INSTANCE", "INST", Flags = CommandFlags.NoUndoMarker,
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

    [CommandMethod("UNLABEL", "ERASE",
        Group = CommandGroup.Label, Scope = CommandScope.Document,
        Summary = "Removes the labels of every point inside the active selection volume.",
        Syntax = "UNLABEL")]
    public IEnumerable<PromptStep> Unlabel(CommandContext context)
    {
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().RemoveSelectionLabels());
        yield break;
    }

    [CommandMethod("SAVELABELS", "QLSAVE", Flags = CommandFlags.NoUndoMarker,
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

    [CommandMethod("LOADLABELS", "QLLOAD",
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
