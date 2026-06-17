using CloudScope.Selection;
using CloudScope.Commands;
using System.Globalization;

namespace CloudScope.Ui.Commands;

public sealed class ViewerCommands : ICommandCancellationHandler
{
    // 0 = idle, 1 = awaiting shape prompt, 2 = tool active
    private int _selectPhase;
    private ZoomPhase _zoomPhase;
    private ScreenPoint _zoomFirstCorner;

    [CommandMethod("SELECT", "SEL")]
    public CommandResult Select(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string option = context.Input.ToUpperInvariant();

        // Sync phase with authoritative controller state: if tool was cancelled externally
        // (e.g. UNDO, Escape key) while we thought we were in phase 2, reset.
        if (_selectPhase == 2 && !viewer.HasActiveSelection)
            _selectPhase = 0;

        if (option.Length == 0)
        {
            if (viewer.HasActiveSelection)
            {
                viewer.ConfirmActiveSelection();
                _selectPhase = 0;
                return CommandResult.End("Selection applied.");
            }

            if (_selectPhase == 2)
                return CommandResult.Continue("Adjust selection using grips or [Confirm/Undo/Cancel]:");

            if (_selectPhase == 1)
            {
                _selectPhase = 0;
                return StartTool(context, SelectionToolType.Box);
            }

            _selectPhase = 1;
            return CommandResult.Continue("Specify selection shape [Box/Sphere/Cylinder/Undo] <Box>:");
        }

        _selectPhase = 0;
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
                    viewer.UndoSelectionCommand() ? "Last selection change undone." : "Nothing to undo.");
            case "CONFIRM":
                if (!viewer.HasActiveSelection)
                    return CommandResult.Continue("Adjust selection using grips or [Confirm/Undo/Cancel]:", "No active selection.");
                viewer.ConfirmActiveSelection();
                return CommandResult.End("Selection applied.");
            case "CANCEL":
            case "ESC":
                viewer.CancelOrExitLabelMode();
                return CommandResult.Cancel();
            default:
                _selectPhase = 1;
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
        var viewer = context.GetTarget<ViewerController>();

        if (_zoomPhase == ZoomPhase.FirstCorner)
        {
            if (TryParseScreenPoint(option, out _zoomFirstCorner))
            {
                _zoomPhase = ZoomPhase.OppositeCorner;
                return CommandResult.Continue("Specify opposite corner:");
            }

            return CommandResult.Continue("Specify first corner:", $"Invalid point: {context.Input}. Use x,y.");
        }

        if (_zoomPhase == ZoomPhase.OppositeCorner)
        {
            if (TryParseScreenPoint(option, out ScreenPoint oppositeCorner))
            {
                viewer.ZoomWindow(_zoomFirstCorner.X, _zoomFirstCorner.Y, oppositeCorner.X, oppositeCorner.Y);
                _zoomPhase = ZoomPhase.None;
                return CommandResult.End("Zoom window.");
            }

            return CommandResult.Continue("Specify opposite corner:", $"Invalid point: {context.Input}. Use x,y.");
        }

        if (option.Length == 0)
            return CommandResult.Continue(
                "Specify corner of window, enter a scale factor (nX or nXP), or [All/Center/Extents/Object] <real time>:");

        if (TryParseWindowArguments(option, out ScreenPoint firstCorner, out ScreenPoint secondCorner))
        {
            viewer.ZoomWindow(firstCorner.X, firstCorner.Y, secondCorner.X, secondCorner.Y);
            return CommandResult.End("Zoom window.");
        }

        string[] parts = option.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string keyword = parts[0].ToUpperInvariant();
        switch (keyword)
        {
            case "A":
            case "ALL":
            case "E":
            case "EXTENTS":
                viewer.ZoomExtents();
                return CommandResult.End("Zoom extents.");
            case "C":
            case "CENTER":
            case "O":
            case "OBJECT":
                viewer.ZoomCenter();
                return CommandResult.End("Zoomed to object at viewport center.");
            case "W":
            case "WINDOW":
                if (parts.Length == 2 && TryParseWindowArguments(parts[1], out firstCorner, out secondCorner))
                {
                    viewer.ZoomWindow(firstCorner.X, firstCorner.Y, secondCorner.X, secondCorner.Y);
                    return CommandResult.End("Zoom window.");
                }

                _zoomPhase = ZoomPhase.FirstCorner;
                return CommandResult.Continue("Specify first corner:");
        }

        if (!TryParseZoomFactor(option, out float factor, out string error))
            return CommandResult.Continue(
                "Specify corner of window, enter a scale factor (nX or nXP), or [All/Center/Extents/Object] <real time>:",
                error);

        viewer.ZoomByFactor(factor);
        return CommandResult.End($"Zoom scale factor: {factor.ToString("0.###", CultureInfo.InvariantCulture)}x");
    }

    [CommandMethod("VIEW", "V", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult View(CommandContext context)
    {
        if (context.Input.Length == 0)
            return CommandResult.Continue("Enter view [Front/Back/Left/Right/Top/Bottom/Isometric]:");

        string view = context.Input.ToUpperInvariant() switch
        {
            "F"        => "FRONT",
            "B"        => "BACK",
            "L"        => "LEFT",
            "R"        => "RIGHT",
            "T"        => "TOP",
            "BOT"      => "BOTTOM",
            "I" or "ISO" => "ISOMETRIC",
            var v      => v
        };

        if (!ValidViews.Contains(view))
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

    [CommandMethod("RESET", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Reset(CommandContext context)
    {
        context.GetTarget<ViewerController>().Reset();
        return CommandResult.End("Viewer reset.");
    }

    [CommandMethod("POINTSIZE", "PSIZE", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult PointSize(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string input = context.Input.Trim();
        if (input.Length == 0)
            return CommandResult.Continue($"Enter point size <{viewer.PointSize:0.0}>:");

        float size;
        if (input == "+")
            size = viewer.PointSize + 0.5f;
        else if (input == "-")
            size = viewer.PointSize - 0.5f;
        else if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            return CommandResult.Continue("Enter point size:", $"Invalid point size: {input}");

        viewer.SetPointSize(size);
        return CommandResult.End($"Point size: {viewer.PointSize:0.0}");
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
        var viewer = context.GetTarget<ViewerController>();
        if (!viewer.HasActiveSelection)
            return CommandResult.End("No active selection.");
        viewer.ConfirmActiveSelection();
        return CommandResult.End("Selection applied.");
    }

    [CommandMethod("CANCEL", "ESC", "ESCAPE", Flags = CommandFlags.NoHistory | CommandFlags.NoUndoMarker)]
    public CommandResult Cancel(CommandContext context)
    {
        context.GetTarget<ViewerController>().CancelOrExitLabelMode();
        return CommandResult.Cancel();
    }

    [CommandMethod("HELP", "?", Flags = CommandFlags.NoUndoMarker | CommandFlags.Transparent)]
    public CommandResult Help(CommandContext context) => CommandResult.End(
        "Commands: SELECT, ZOOM, VIEW, PROJECTION, POINTSIZE, LABEL, SAVELABELS, LOADLABELS, CLEARLABELS, NAVIGATE, RESET, UNDO, HELP.");

    public void CancelCommand(CommandContext context, string globalCommandName)
    {
        if (globalCommandName == "ZOOM")
        {
            _zoomPhase = ZoomPhase.None;
            return;
        }

        if (globalCommandName != "SELECT")
            return;

        context.GetTarget<ViewerController>().CancelOrExitLabelMode();
        _selectPhase = 0;
    }

    private CommandResult StartTool(CommandContext context, SelectionToolType tool)
    {
        context.GetTarget<ViewerController>().SetTool(tool);
        _selectPhase = 2;
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

    private static bool TryParseWindowArguments(string input, out ScreenPoint first, out ScreenPoint second)
    {
        first = default;
        second = default;
        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
            TryParseScreenPoint(parts[0], out first) &&
            TryParseScreenPoint(parts[1], out second);
    }

    private static bool TryParseScreenPoint(string input, out ScreenPoint point)
    {
        point = default;
        string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            return false;

        point = new ScreenPoint((int)MathF.Round(x), (int)MathF.Round(y));
        return true;
    }

    private enum ZoomPhase { None, FirstCorner, OppositeCorner }

    private readonly record struct ScreenPoint(int X, int Y);

    private static readonly HashSet<string> ValidViews =
        new(["FRONT", "BACK", "LEFT", "RIGHT", "TOP", "BOTTOM", "ISOMETRIC"], StringComparer.Ordinal);
}
