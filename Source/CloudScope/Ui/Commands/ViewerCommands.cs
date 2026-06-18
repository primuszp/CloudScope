using CloudScope.Selection;
using CloudScope.Commands;
using CloudScope.Loading;
using System.Globalization;

namespace CloudScope.Ui.Commands;

public sealed class ViewerCommands : ICommandCancellationHandler
{
    // 0 = idle, 1 = awaiting shape prompt, 2 = tool active
    private int _selectPhase;
    private ZoomPhase _zoomPhase;
    private ScreenPoint _zoomFirstCorner;
    private FilterPhase _filterPhase;
    private FilterAttribute _pendingFilterAttribute;
    private bool _colorPromptActive;

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

    [CommandMethod("OPEN", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Open(CommandContext context)
    {
        if (!TryParseOpenArguments(context.Input, out string path, out long maxPoints, out string error))
            return CommandResult.Continue("Enter LAS file path:", error);

        return CommandResult.End(context.GetTarget<ViewerController>().OpenPointCloud(path, maxPoints));
    }

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

    [CommandMethod("ATTRIBUTES", "ATTRS", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Attributes(CommandContext context)
    {
        string input = context.Input.Trim();
        if (input.Length == 0)
            return CommandResult.End(context.GetTarget<ViewerController>().DescribeAttributes("ALL"));

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return CommandResult.End(context.GetTarget<ViewerController>().DescribeAttributes(parts[0]));
    }

    [CommandMethod("FILTER", "FI", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Filter(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string input = context.Input.Trim();

        if (_filterPhase == FilterPhase.Values)
            return CompleteFilterValuePrompt(viewer, input);

        if (_filterPhase == FilterPhase.Attribute && input.Length == 0)
        {
            _filterPhase = FilterPhase.Values;
            _pendingFilterAttribute = FilterAttribute.Class;
            return CommandResult.Continue(GetFilterValuePrompt(FilterAttribute.Class));
        }

        if (input.Length == 0)
        {
            _filterPhase = FilterPhase.Attribute;
            return CommandResult.Continue("Select filter attribute [Class/Intensity/Return/Z/Clear] <Class>:");
        }

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!TryParseFilterAttribute(parts[0], out FilterAttribute attribute, out string attributeError))
            return CommandResult.Continue("Select filter attribute [Class/Intensity/Return/Z/Clear] <Class>:", attributeError);

        if (attribute == FilterAttribute.Clear)
        {
            _filterPhase = FilterPhase.None;
            return CommandResult.End(viewer.ApplyPointFilter(null));
        }

        if (parts.Length == 1)
        {
            _filterPhase = FilterPhase.Values;
            _pendingFilterAttribute = attribute;
            return CommandResult.Continue(GetFilterValuePrompt(attribute));
        }

        return ApplyParsedFilter(viewer, attribute, parts[1..]);
    }

    [CommandMethod("COLORBY", "COLOR", "CB", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult ColorBy(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string input = context.Input.Trim();
        if (input.Length == 0 && !_colorPromptActive)
        {
            _colorPromptActive = true;
            return CommandResult.Continue("Select color source [Rgb/Height/Class/Intensity/Return/Clear] <Rgb>:");
        }

        if (input.Length == 0)
            input = "RGB";

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!TryParseColorSource(parts[0], out ColorSource source, out bool clear, out string error))
            return CommandResult.Continue("Select color source [Rgb/Height/Class/Intensity/Return/Clear] <Rgb>:", error);

        _colorPromptActive = false;
        return CommandResult.End(clear ? viewer.ClearColorSource() : viewer.SetColorSource(source));
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
        "Commands: OPEN, SELECT, FILTER, COLORBY, ATTRS, ZOOM, VIEW, PROJECTION, POINTSIZE, LABEL, SAVELABELS, LOADLABELS, CLEARLABELS, NAVIGATE, RESET, UNDO, HELP.");

    public void CancelCommand(CommandContext context, string globalCommandName)
    {
        if (globalCommandName == "ZOOM")
        {
            _zoomPhase = ZoomPhase.None;
            return;
        }

        if (globalCommandName == "FILTER")
        {
            _filterPhase = FilterPhase.None;
            return;
        }

        if (globalCommandName is "COLORBY" or "COLOR")
        {
            _colorPromptActive = false;
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

    private static bool TryParseOpenArguments(string input, out string path, out long maxPoints, out string error)
    {
        path = "";
        maxPoints = 50_000_000;
        error = "";

        string trimmed = input.Trim();
        if (trimmed.Length == 0)
            return false;

        string remaining;
        if (trimmed[0] == '"')
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote < 0)
            {
                error = "Missing closing quote in file path.";
                return false;
            }

            path = trimmed[1..endQuote];
            remaining = trimmed[(endQuote + 1)..].Trim();
        }
        else
        {
            string[] parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            path = parts[0];
            remaining = parts.Length == 2 ? parts[1] : "";
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "OPEN requires a file path.";
            return false;
        }

        if (remaining.Length > 0 && (!long.TryParse(remaining, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxPoints) || maxPoints <= 0))
        {
            error = $"Invalid max point count: {remaining}";
            return false;
        }

        return true;
    }

    private CommandResult CompleteFilterValuePrompt(ViewerController viewer, string input)
    {
        if (input.Length == 0 && _pendingFilterAttribute == FilterAttribute.Class)
            input = "L";

        if (IsBack(input))
        {
            _filterPhase = FilterPhase.Attribute;
            return CommandResult.Continue("Select filter attribute [Class/Intensity/Return/Z/Clear] <Class>:");
        }

        if (IsList(input))
            return CommandResult.Continue(GetFilterValuePrompt(_pendingFilterAttribute), viewer.DescribeAttributes(FilterAttributeToCommandName(_pendingFilterAttribute)));

        _filterPhase = FilterPhase.None;
        return ApplyParsedFilter(viewer, _pendingFilterAttribute, input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static CommandResult ApplyParsedFilter(ViewerController viewer, FilterAttribute attribute, string[] values)
    {
        if (!TryCreateFilter(attribute, values, out PointFilter? filter, out string error))
            return CommandResult.Continue(GetFilterValuePrompt(attribute), error);

        return CommandResult.End(viewer.ApplyPointFilter(filter));
    }

    private static bool TryCreateFilter(FilterAttribute attribute, string[] values, out PointFilter? filter, out string error)
    {
        filter = null;
        error = "";

        string joined = string.Join(",", values).Replace(";", ",", StringComparison.Ordinal);
        string[] tokens = joined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        switch (attribute)
        {
            case FilterAttribute.Class:
                if (!TryParseByteList(tokens, out byte[] classes))
                {
                    error = "Enter class values like 3 or 2,3,5.";
                    return false;
                }
                filter = new ClassFilter(classes);
                return true;
            case FilterAttribute.Return:
                if (!TryParseByteList(tokens, out byte[] returns))
                {
                    error = "Enter return values like 1 or 1,2.";
                    return false;
                }
                filter = new ReturnFilter(returns);
                return true;
            case FilterAttribute.Intensity:
                if (values.Length != 2 ||
                    !ushort.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort minIntensity) ||
                    !ushort.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort maxIntensity))
                {
                    error = "Enter intensity range as: min max.";
                    return false;
                }
                if (minIntensity > maxIntensity)
                    (minIntensity, maxIntensity) = (maxIntensity, minIntensity);
                filter = new IntensityFilter(minIntensity, maxIntensity);
                return true;
            case FilterAttribute.Z:
                if (values.Length != 2 ||
                    !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double minZ) ||
                    !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double maxZ))
                {
                    error = "Enter Z range as: min max.";
                    return false;
                }
                if (minZ > maxZ)
                    (minZ, maxZ) = (maxZ, minZ);
                filter = new ZFilter(minZ, maxZ);
                return true;
            default:
                error = "Unsupported filter attribute.";
                return false;
        }
    }

    private static bool TryParseByteList(string[] tokens, out byte[] values)
    {
        values = [];
        var parsed = new List<byte>();
        foreach (string token in tokens)
        {
            if (!byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value))
                return false;
            parsed.Add(value);
        }

        values = parsed.Distinct().ToArray();
        return values.Length > 0;
    }

    private static bool TryParseFilterAttribute(string input, out FilterAttribute attribute, out string error)
    {
        attribute = input.ToUpperInvariant() switch
        {
            "" or "C" or "CLASS" => FilterAttribute.Class,
            "I" or "INTENSITY" => FilterAttribute.Intensity,
            "R" or "RETURN" => FilterAttribute.Return,
            "Z" or "HEIGHT" => FilterAttribute.Z,
            "CL" or "CLEAR" => FilterAttribute.Clear,
            _ => FilterAttribute.Unknown
        };

        error = attribute == FilterAttribute.Unknown ? $"Invalid filter attribute: {input}" : "";
        return attribute != FilterAttribute.Unknown;
    }

    private static bool TryParseColorSource(string input, out ColorSource source, out bool clear, out string error)
    {
        clear = false;
        source = input.ToUpperInvariant() switch
        {
            "" or "R" or "RGB" => ColorSource.Rgb,
            "H" or "HEIGHT" or "Z" => ColorSource.Height,
            "C" or "CLASS" => ColorSource.Class,
            "I" or "INTENSITY" => ColorSource.Intensity,
            "RT" or "RETURN" => ColorSource.Return,
            "CL" or "CLEAR" => ColorSource.Rgb,
            _ => ColorSource.Rgb
        };

        clear = input.Equals("CL", StringComparison.OrdinalIgnoreCase) || input.Equals("CLEAR", StringComparison.OrdinalIgnoreCase);
        error = source == ColorSource.Rgb && !clear && !input.Equals("", StringComparison.Ordinal) &&
            !input.Equals("R", StringComparison.OrdinalIgnoreCase) &&
            !input.Equals("RGB", StringComparison.OrdinalIgnoreCase)
            ? $"Invalid color source: {input}"
            : "";
        return error.Length == 0;
    }

    private static string GetFilterValuePrompt(FilterAttribute attribute) => attribute switch
    {
        FilterAttribute.Class => "Enter class values or [List/Back] <List>:",
        FilterAttribute.Intensity => "Enter intensity range min max or [List/Back]:",
        FilterAttribute.Return => "Enter return values or [List/Back]:",
        FilterAttribute.Z => "Enter Z range min max or [List/Back]:",
        _ => "Enter filter value:"
    };

    private static bool IsList(string input) => input.Equals("L", StringComparison.OrdinalIgnoreCase) || input.Equals("LIST", StringComparison.OrdinalIgnoreCase);
    private static bool IsBack(string input) => input.Equals("B", StringComparison.OrdinalIgnoreCase) || input.Equals("BACK", StringComparison.OrdinalIgnoreCase);

    private static string FilterAttributeToCommandName(FilterAttribute attribute) => attribute switch
    {
        FilterAttribute.Class => "Class",
        FilterAttribute.Intensity => "Intensity",
        FilterAttribute.Return => "Return",
        FilterAttribute.Z => "Z",
        _ => "All"
    };

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
    private enum FilterPhase { None, Attribute, Values }
    private enum FilterAttribute { Unknown, Class, Intensity, Return, Z, Clear }

    private readonly record struct ScreenPoint(int X, int Y);

    private static readonly HashSet<string> ValidViews =
        new(["FRONT", "BACK", "LEFT", "RIGHT", "TOP", "BOTTOM", "ISOMETRIC"], StringComparer.Ordinal);
}
