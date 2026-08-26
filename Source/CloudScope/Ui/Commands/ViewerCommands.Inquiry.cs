using CloudScope.Commands;
using CloudScope.Rendering;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] UndoKeywords =
    [
        new("MARK", "Mark"),
        new("BACK", "Back")
    ];

    [CommandMethod("STATUS", Flags = CommandFlags.NoHistory | CommandFlags.NoUndoMarker | CommandFlags.Transparent,
        Group = CommandGroup.Inquiry, Scope = CommandScope.Viewer,
        Summary = "Reports what the viewer is currently showing and doing.",
        Syntax = "STATUS")]
    public IEnumerable<PromptStep> Status(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        ViewerStatusSnapshot status = viewer.Status;

        context.Editor.WriteMessage(
            $"Source        {(status.SourceName.Length > 0 ? status.SourceName : "(none)")}"
            + Environment.NewLine + $"Points        {status.PointCountText}"
            + Environment.NewLine + $"Filter        {(status.Filter.Length > 0 ? status.Filter : "None")}"
            + Environment.NewLine + $"Colour by     {status.ColorSource}"
            + Environment.NewLine + $"Mode          {status.Mode} / {status.ActiveTool}"
            + Environment.NewLine + $"Label         {status.CurrentLabel} (instance {status.InstanceText})"
            + Environment.NewLine + $"View          {status.ViewName}, {status.ProjectionText}, {status.ViewportLayout}"
            + Environment.NewLine + $"Point size    {status.PointSize:0.0} px"
            + Environment.NewLine + $"Undo depth    {viewer.UndoHistory.Depth}");
        yield break;
    }

    [CommandMethod("ATTRIBUTES", "ATTRS", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory,
        Group = CommandGroup.Inquiry, Scope = CommandScope.Document,
        Summary = "Reports the distribution of an attribute across the cloud.",
        Syntax = "ATTRIBUTES [All/Class/Intensity/Return/Z]")]
    public IEnumerable<PromptStep> Attributes(CommandContext context)
    {
        // Plain ATTRIBUTES reports everything, as it always has.
        if (!context.Editor.HasArguments)
        {
            context.Editor.WriteMessage(context.GetTarget<ViewerController>().DescribeAttributes("ALL"));
            yield break;
        }

        PromptStringStep attribute = context.Editor
            .GetString("Enter attribute [All/Class/Intensity/Return/Z] <All>:")
            .WithDefault("ALL");
        yield return attribute;

        string name = attribute.IsOk && attribute.Value.Length > 0 ? attribute.Value : "ALL";
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().DescribeAttributes(name));
    }

    [CommandMethod("TIME", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory | CommandFlags.Transparent,
        Group = CommandGroup.Inquiry, Scope = CommandScope.Viewer,
        Summary = "Reports frame rate and point throughput.",
        Syntax = "TIME")]
    public IEnumerable<PromptStep> Time(CommandContext context)
    {
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().DescribeFrameTiming());
        yield break;
    }

    [CommandMethod("HISTORY", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory | CommandFlags.Transparent,
        Group = CommandGroup.Inquiry, Scope = CommandScope.Viewer,
        Summary = "Shows or hides the expanded command history window.",
        Syntax = "HISTORY")]
    public IEnumerable<PromptStep> History(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        viewer.CommandHistoryVisible = !viewer.CommandHistoryVisible;
        context.Editor.WriteMessage($"Command history {(viewer.CommandHistoryVisible ? "shown" : "hidden")}.");
        yield break;
    }

    [CommandMethod("UNDO", "U", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Steps back through completed commands.",
        Syntax = "UNDO [<number>/Mark/Back]")]
    public IEnumerable<PromptStep> Undo(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        // A volume still being drawn is put away first: that is what the user means by
        // "undo" while a selection is on screen, and it consumes no history step.
        if (viewer.HasActiveSelection)
        {
            ed.WriteMessage(viewer.UndoSelectionCommand() ? "Current selection undone." : "Nothing to undo.");
            yield break;
        }

        PromptIntegerStep count = ed
            .GetInteger("Enter number of operations to undo or [Mark/Back] <1>:")
            .WithRange(1, int.MaxValue)
            .WithDefault(1)
            .WithKeywords(UndoKeywords);
        yield return count;

        UndoManager history = viewer.UndoHistory;

        if (count.Is("MARK"))
        {
            history.SetMark();
            ed.WriteMessage("Undo mark set.");
            yield break;
        }

        if (count.Is("BACK"))
        {
            int back = history.Back();
            ed.WriteMessage(back < 0
                ? "No undo mark has been set."
                : back == 0 ? "Nothing to undo." : $"Undid {back} operation(s) back to the mark.");
            yield break;
        }

        if (!count.IsOk) yield break;

        int done = history.Undo(count.Value);
        ed.WriteMessage(done == 0 ? "Nothing to undo." : $"Undid {done} operation(s).");
    }

    [CommandMethod("REDO", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Reapplies commands stepped back with UNDO.",
        Syntax = "REDO [<number>]")]
    public IEnumerable<PromptStep> Redo(CommandContext context)
    {
        PromptIntegerStep count = context.Editor
            .GetInteger("Enter number of operations to redo <1>:")
            .WithRange(1, int.MaxValue)
            .WithDefault(1);
        yield return count;
        if (!count.IsOk) yield break;

        int done = context.GetTarget<ViewerController>().UndoHistory.Redo(count.Value);
        context.Editor.WriteMessage(done == 0 ? "Nothing to redo." : $"Redid {done} operation(s).");
    }

    [CommandMethod("SETVAR", "SET", Flags = CommandFlags.NoUndoMarker | CommandFlags.Transparent,
        Group = CommandGroup.Settings, Scope = CommandScope.Viewer,
        Summary = "Lists or changes a system variable.",
        Syntax = "SETVAR <name> <value> | ? [pattern]")]
    public IEnumerable<PromptStep> SetVar(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStringStep name = ed.GetString("Enter variable name or [?] to list:");
        yield return name;
        if (!name.IsOk) yield break;

        if (name.Value == "?")
        {
            PromptStringStep pattern = ed.GetString("Enter variable(s) to list <*>:").WithDefault("*");
            yield return pattern;

            IEnumerable<SystemVariable> matches = viewer.Variables.Match(pattern.IsOk ? pattern.Value : "*");
            string listing = string.Join(Environment.NewLine, matches.Select(v => v.ListLine));
            ed.WriteMessage(listing.Length == 0 ? "No matching system variables." : listing);
            yield break;
        }

        if (!viewer.Variables.TryGet(name.Value, out SystemVariable variable))
        {
            ed.WriteMessage($"Unknown system variable: {name.Value}");
            yield break;
        }

        if (variable.IsReadOnly)
        {
            ed.WriteMessage($"{variable.Name} = {variable.Value} (read-only)");
            yield break;
        }

        string allowed = variable.AllowedValues.Count > 0
            ? $" [{string.Join('/', variable.AllowedValues)}]"
            : "";
        PromptStringStep value = ed.GetLine($"Enter new value for {variable.Name}{allowed} <{variable.Value}>:");
        yield return value;
        if (!value.IsOk) yield break;

        ed.WriteMessage(viewer.Variables.Write(variable.Name, value.Value));
    }

    [CommandMethod("GETVAR", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory | CommandFlags.Transparent,
        Group = CommandGroup.Settings, Scope = CommandScope.Viewer,
        Summary = "Reports the value of a system variable.",
        Syntax = "GETVAR <name>")]
    public IEnumerable<PromptStep> GetVar(CommandContext context)
    {
        PromptStringStep name = context.Editor.GetString("Enter variable name:");
        yield return name;
        if (!name.IsOk) yield break;

        context.Editor.WriteMessage(context.GetTarget<ViewerController>().Variables.Read(name.Value));
    }

    [CommandMethod("GRAPHICSCONFIG", "3DCONFIG", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Settings, Scope = CommandScope.Viewer,
        Summary = "Reports the rendering backend and how to select one.",
        Syntax = "GRAPHICSCONFIG")]
    public IEnumerable<PromptStep> GraphicsConfig(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        RenderBackendKind available = RenderBackendFactory.GetDefaultKind();

        context.Editor.WriteMessage(
            $"Backend in use    {viewer.RenderBackendName}"
            + Environment.NewLine + $"Default for host  {available}"
            + Environment.NewLine + "The backend is chosen when the viewer starts. Set CLOUDSCOPE_RENDER_BACKEND"
            + Environment.NewLine + "to opengl or metal and restart to change it.");
        yield break;
    }

    [CommandMethod("POINTCLOUDCONFIG", "PTCONFIG", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Settings, Scope = CommandScope.Viewer,
        Summary = "Shows or sets the per-frame and resident point budgets.",
        Syntax = "POINTCLOUDCONFIG [Frame <points>/Resident <points>/Show]")]
    public IEnumerable<PromptStep> PointCloudConfig(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStep option = ed
            .GetKeywords("Point cloud budget [Frame/Resident/Show] <Show>:",
                new Keyword("FRAME", "Frame"), new Keyword("RESIDENT", "Resident"), new Keyword("SHOW", "Show"))
            .WithDefaultKeyword("SHOW");
        yield return option;

        if (option.Is("SHOW") || option.Status != PromptStatus.Keyword)
        {
            ed.WriteMessage(
                viewer.Variables.Read("PTMAX")
                + Environment.NewLine + viewer.Variables.Read("PTRESIDENT")
                + Environment.NewLine + "PTRESIDENT = 0 means the environment's own limit.");
            yield break;
        }

        string variable = option.Is("FRAME") ? "PTMAX" : "PTRESIDENT";
        PromptIntegerStep value = ed
            .GetInteger($"Enter {variable} in points:")
            .WithRange(0, int.MaxValue);
        yield return value;
        if (!value.IsOk) yield break;

        ed.WriteMessage(viewer.Variables.Write(variable, value.Value.ToString()));
    }

    [CommandMethod("SCRIPT", "SCR", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Utility, Scope = CommandScope.Application,
        Summary = "Runs a file of commands, one per line.",
        Syntax = "SCRIPT <path>")]
    public IEnumerable<PromptStep> Script(CommandContext context)
    {
        Editor ed = context.Editor;
        PromptFileStep path = ed.GetFileNameForOpen("Enter script file path:").Filtering("scr").Requiring();
        yield return path;
        if (!path.IsOk) yield break;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path.Value);
        }
        catch (IOException ex)
        {
            ed.WriteMessage($"Could not read script: {ex.Message}");
            yield break;
        }

        int queued = 0;
        foreach (string raw in lines)
        {
            // ";" starts a comment, as it does in an AutoCAD script.
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            ed.RunAfter(line);
            queued++;
        }

        ed.WriteMessage($"Running {queued} line(s) from {Path.GetFileName(path.Value)}.");
    }

    [CommandMethod("QUIT", "EXIT", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Utility, Scope = CommandScope.Application,
        Summary = "Closes the viewer.",
        Syntax = "QUIT")]
    public IEnumerable<PromptStep> Quit(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        // Only ask when there is something to lose.
        if (HasUnsavedLabels(viewer))
        {
            PromptStep save = ed
                .GetKeywords("Save labels before quitting? [Yes/No/Cancel] <Yes>:",
                    new Keyword("YES", "Yes"), new Keyword("NO", "No"), new Keyword("CANCEL", "Cancel"))
                .WithDefaultKeyword("YES");
            yield return save;

            if (save.Is("CANCEL") || save.Status != PromptStatus.Keyword)
            {
                ed.Cancel();
                yield break;
            }

            if (save.Is("YES"))
                ed.WriteMessage(viewer.SaveLabels() ? "Labels saved." : "Labels could not be saved.");
        }

        viewer.RequestClose();
        ed.WriteMessage("Closing CloudScope.");
    }

    private static bool HasUnsavedLabels(ViewerController viewer) => viewer.LabelledPointCount > 0;

    [CommandMethod("COVERAGE", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory | CommandFlags.Transparent,
        Group = CommandGroup.Utility, Scope = CommandScope.Application,
        Summary = "Reports which viewer capabilities no command can reach.",
        Syntax = "COVERAGE")]
    public IEnumerable<PromptStep> Coverage(CommandContext context)
    {
        if (Executor == null)
        {
            context.Editor.WriteMessage("The command table is not available.");
            yield break;
        }

        context.Editor.WriteMessage(
            CommandCoverage.Analyse(Executor.Commands, typeof(ViewerController)).Describe());
    }

    [CommandMethod("HELP", "?", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory | CommandFlags.Transparent,
        Group = CommandGroup.Utility, Scope = CommandScope.Application,
        Summary = "Lists the commands, or explains one of them.",
        Syntax = "HELP [command]")]
    public IEnumerable<PromptStep> Help(CommandContext context)
    {
        Editor ed = context.Editor;
        if (Executor == null)
        {
            ed.WriteMessage("Type a command name, or press F1 for the command list.");
            yield break;
        }

        // Plain HELP (F1, the menu) lists everything rather than asking a question first.
        if (!ed.HasArguments)
        {
            ed.WriteMessage(DescribeCommandList(Executor));
            yield break;
        }

        PromptStringStep topic = ed.GetString("Enter command name for help <list all>:").WithDefault("");
        yield return topic;

        string name = topic.IsOk ? topic.Value.Trim() : "";
        if (name.Length == 0)
        {
            ed.WriteMessage(DescribeCommandList(Executor));
            yield break;
        }

        if (!Executor.TryGetCommand(name, out CommandDescriptor command))
        {
            ed.WriteMessage($"Unknown command \"{name}\". Type HELP for the command list.");
            yield break;
        }

        ed.WriteMessage(DescribeCommand(command));
    }

    /// <summary>
    /// The command list, grouped and generated from the command table. Nothing here is
    /// hand-maintained, so a new command appears in HELP by existing.
    /// </summary>
    private static string DescribeCommandList(ICommandExecutor executor)
    {
        IEnumerable<IGrouping<CommandGroup, CommandDescriptor>> groups = executor.Commands
            .GroupBy(command => command.Group)
            .OrderBy(group => group.Key);

        var lines = new List<string>();
        foreach (IGrouping<CommandGroup, CommandDescriptor> group in groups)
        {
            lines.Add($"{group.Key}:");
            lines.AddRange(group
                .OrderBy(command => command.GlobalName, StringComparer.OrdinalIgnoreCase)
                .Select(command => "  " + command.ListLine));
        }

        lines.Add("");
        lines.Add("Type HELP <command> for its options.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeCommand(CommandDescriptor command)
    {
        var lines = new List<string> { command.GlobalName, "  " + command.Summary, "  Usage:  " + command.UsageLine };

        if (command.Aliases.Count > 0)
            lines.Add("  Alias:  " + command.AliasText);

        lines.Add($"  Group:  {command.Group}   Needs: {command.Scope}");

        var traits = new List<string>();
        if (command.Flags.HasFlag(CommandFlags.Transparent)) traits.Add("runs inside another command");
        if (command.Flags.HasFlag(CommandFlags.NoUndoMarker)) traits.Add("no undo step");
        if (command.Flags.HasFlag(CommandFlags.NoHistory)) traits.Add("not repeated by Enter");
        if (traits.Count > 0)
            lines.Add("  Notes:  " + string.Join(", ", traits));

        return string.Join(Environment.NewLine, lines);
    }
}
