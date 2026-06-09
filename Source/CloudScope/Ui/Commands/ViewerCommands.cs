using CloudScope.Selection;
using CloudScope.Commands;
using System.Globalization;

namespace CloudScope.Ui.Commands;

public sealed class ViewerCommands : ICommandCancellationHandler
{
    private bool _selectHasTool;
    private bool _selectAwaitingShape;

    [CommandMethod("SELECT", "SEL")]
    public CommandResult Select(CommandContext context)
    {
        string option = context.Input.ToUpperInvariant();
        if (option.Length == 0)
        {
            if (context.GetTarget<ViewerController>().HasActiveSelection)
            {
                context.GetTarget<ViewerController>().ConfirmActiveSelection();
                _selectHasTool = false;
                _selectAwaitingShape = false;
                return CommandResult.End("Selection applied.");
            }

            if (_selectHasTool)
                return CommandResult.Continue("Adjust selection using grips or [Confirm/Undo/Cancel]:");

            if (_selectAwaitingShape)
            {
                _selectAwaitingShape = false;
                return StartTool(context, SelectionToolType.Box);
            }

            _selectAwaitingShape = true;
            return CommandResult.Continue("Specify selection shape [Box/Sphere/Cylinder/Undo] <Box>:");
        }

        _selectAwaitingShape = false;
        switch (option)
        {
            case "B":
            case "BOX":
                return StartTool(context, SelectionToolType.Box);
            case "S":
            case "SPH":
            case "SPHERE":
                return StartTool(context, SelectionToolType.Sphere);
            case "C":
            case "CYL":
            case "CYLINDER":
                return StartTool(context, SelectionToolType.Cylinder);
            case "U":
            case "UNDO":
                return CommandResult.Continue(
                    "Adjust selection using grips or [Confirm/Undo/Cancel]:",
                    context.GetTarget<ViewerController>().UndoSelectionCommand() ? "Last selection change undone." : "Nothing to undo.");
            case "CONFIRM":
                if (!context.GetTarget<ViewerController>().HasActiveSelection)
                    return CommandResult.Continue("Adjust selection using grips or [Confirm/Undo/Cancel]:", "No active selection.");
                context.GetTarget<ViewerController>().ConfirmActiveSelection();
                _selectHasTool = false;
                _selectAwaitingShape = false;
                return CommandResult.End("Selection applied.");
            case "CANCEL":
            case "ESC":
                context.GetTarget<ViewerController>().CancelOrExitLabelMode();
                _selectHasTool = false;
                _selectAwaitingShape = false;
                return CommandResult.Cancel();
            default:
                return CommandResult.Continue(
                    "Specify selection shape [Box/Sphere/Cylinder/Undo] <Box>:",
                    $"Invalid option keyword: {context.Input}");
        }
    }

    [CommandMethod("NAVIGATE", "NAV", "N", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Navigate(CommandContext context)
    {
        context.GetTarget<ViewerController>().SetMode(InteractionMode.Navigate);
        return CommandResult.End("Mode: Navigate");
    }

    [CommandMethod("LABELMODE", "L", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult LabelMode(CommandContext context)
    {
        InteractionMode target = context.GetTarget<ViewerController>().Mode == InteractionMode.Label
            ? InteractionMode.Navigate
            : InteractionMode.Label;
        context.GetTarget<ViewerController>().SetMode(target);
        return CommandResult.End($"Mode: {target}");
    }

    [CommandMethod("LABEL", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Label(CommandContext context)
    {
        if (context.Input.Length == 0)
            return CommandResult.Continue("Enter label name:");
        context.GetTarget<ViewerController>().SetLabel(context.Input);
        return CommandResult.End($"Label: {context.Input}");
    }

    [CommandMethod("UNDO", "U", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Undo(CommandContext context) =>
        CommandResult.End(context.GetTarget<ViewerController>().UndoSelectionCommand() ? "Undo complete." : "Nothing to undo.");

    [CommandMethod("ZOOM", "Z", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Zoom(CommandContext context)
    {
        string option = context.Input.Trim();
        if (option.Length == 0)
            return CommandResult.Continue(
                "Specify corner of window, enter a scale factor (nX or nXP), or [All/Center/Extents/Object] <real time>:");

        switch (option.ToUpperInvariant())
        {
            case "A":
            case "ALL":
            case "E":
            case "EXTENTS":
                context.GetTarget<ViewerController>().ZoomExtents();
                return CommandResult.End("Zoom extents.");
            case "C":
            case "CENTER":
            case "O":
            case "OBJECT":
                context.GetTarget<ViewerController>().ZoomCenter();
                return CommandResult.End("Zoomed to object at viewport center.");
            case "W":
            case "WINDOW":
                return CommandResult.End("ZOOM Window requires interactive point input, which is not available yet.");
        }

        if (!TryParseZoomFactor(option, out float factor, out string error))
            return CommandResult.Continue(
                "Specify corner of window, enter a scale factor (nX or nXP), or [All/Center/Extents/Object] <real time>:",
                error);

        context.GetTarget<ViewerController>().ZoomByFactor(factor);
        return CommandResult.End($"Zoom scale factor: {factor.ToString("0.###", CultureInfo.InvariantCulture)}x");
    }

    [CommandMethod("VIEW", "V", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult View(CommandContext context)
    {
        if (context.Input.Length == 0)
            return CommandResult.Continue("Enter view [Front/Back/Left/Right/Top/Bottom/Isometric]:");

        string view = context.Input.ToUpperInvariant() switch
        {
            "F" => "FRONT",
            "B" => "BACK",
            "L" => "LEFT",
            "R" => "RIGHT",
            "T" => "TOP",
            "BOT" => "BOTTOM",
            "I" or "ISO" => "ISOMETRIC",
            var value => value
        };

        string[] views = ["FRONT", "BACK", "LEFT", "RIGHT", "TOP", "BOTTOM", "ISOMETRIC"];
        if (!views.Contains(view))
            return CommandResult.Continue(
                "Enter view [Front/Back/Left/Right/Top/Bottom/Isometric]:",
                $"Invalid view keyword: {context.Input}");

        context.GetTarget<ViewerController>().SetView(view);
        return CommandResult.End($"View: {view}");
    }

    [CommandMethod("PROJECTION", "PROJ", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Projection(CommandContext context)
    {
        context.GetTarget<ViewerController>().ToggleProjection();
        return CommandResult.End("Projection toggled.");
    }

    [CommandMethod("POINTSIZE", "PSIZE", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult PointSize(CommandContext context)
    {
        string input = context.Input.Trim();
        if (input.Length == 0)
            return CommandResult.Continue($"Enter point size <{context.GetTarget<ViewerController>().PointSize:0.0}>:");

        float size;
        if (input == "+")
            size = context.GetTarget<ViewerController>().PointSize + 0.5f;
        else if (input == "-")
            size = context.GetTarget<ViewerController>().PointSize - 0.5f;
        else if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            return CommandResult.Continue("Enter point size:", $"Invalid point size: {input}");

        context.GetTarget<ViewerController>().SetPointSize(size);
        return CommandResult.End($"Point size: {context.GetTarget<ViewerController>().PointSize:0.0}");
    }

    [CommandMethod("SAVELABELS", "QLSAVE", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult SaveLabels(CommandContext context) =>
        CommandResult.End(context.GetTarget<ViewerController>().SaveLabels() ? "Labels saved." : "No source file is associated with the viewer.");

    [CommandMethod("LOADLABELS", "QLLOAD")]
    public CommandResult LoadLabels(CommandContext context) =>
        CommandResult.End(context.GetTarget<ViewerController>().LoadLabels() ? "Labels loaded." : "Label file could not be loaded.");

    [CommandMethod("CLEARLABELS")]
    public CommandResult ClearLabels(CommandContext context)
    {
        context.GetTarget<ViewerController>().ClearLabels();
        return CommandResult.End("All labels cleared.");
    }

    [CommandMethod("CONFIRM", "ENTER", Flags = CommandFlags.NoHistory)]
    public CommandResult Confirm(CommandContext context)
    {
        if (!context.GetTarget<ViewerController>().HasActiveSelection)
            return CommandResult.End("No active selection.");
        context.GetTarget<ViewerController>().ConfirmActiveSelection();
        return CommandResult.End("Selection applied.");
    }

    [CommandMethod("CANCEL", "ESC", "ESCAPE", Flags = CommandFlags.NoHistory | CommandFlags.NoUndoMarker)]
    public CommandResult Cancel(CommandContext context)
    {
        context.GetTarget<ViewerController>().CancelOrExitLabelMode();
        return CommandResult.Cancel();
    }

    [CommandMethod("HELP", "?", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Help(CommandContext context) => CommandResult.End(
        "Commands: SELECT, ZOOM, VIEW, PROJECTION, POINTSIZE, LABEL, SAVELABELS, LOADLABELS, CLEARLABELS, NAVIGATE, UNDO, HELP.");

    private CommandResult StartTool(CommandContext context, SelectionToolType tool)
    {
        context.GetTarget<ViewerController>().SetTool(tool);
        _selectHasTool = true;
        _selectAwaitingShape = false;
        return CommandResult.Continue(
            "Adjust selection using grips or [Confirm/Undo/Cancel]:",
            $"{tool} selection started. Draw the selection volume.");
    }

    private static bool TryParseZoomFactor(string input, out float factor, out string error)
    {
        string value = input.Trim();
        if (value.EndsWith("XP", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        else if (value.EndsWith('X') || value.EndsWith('x'))
            value = value[..^1];

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out factor) || factor <= 0f)
        {
            error = $"Requires a positive numeric scale factor: {input}";
            return false;
        }

        error = "";
        return true;
    }

    public void CancelCommand(CommandContext context, string globalCommandName)
    {
        if (globalCommandName != "SELECT")
            return;

        context.GetTarget<ViewerController>().CancelOrExitLabelMode();
        _selectHasTool = false;
        _selectAwaitingShape = false;
    }
}

