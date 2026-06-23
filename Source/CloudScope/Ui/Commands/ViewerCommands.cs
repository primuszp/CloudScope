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
    private bool _vportsLayoutPrompt;
    private bool _vportsArrangementPrompt;
    private bool _vportsViewPrompt;
    private ViewportLayoutKind _pendingViewportLayout;
    private ViewportLayoutKind _lastViewportLayout = ViewportLayoutKind.SinglePerspective;
    private ViewportLayoutKind _lastViewportArrangement = ViewportLayoutKind.TwoVertical;
    private ViewportViewKind _lastViewportView = ViewportViewKind.Top;

    // ----- Keyword sets (AutoCAD-style: capitalised letters define the abbreviation) -----

    private static readonly Keyword[] SelectKeywords =
    [
        new("BOX", "Box"),
        new("SPHERE", "Sphere", "SPH"),
        new("CYLINDER", "Cylinder", "CYL"),
        new("UNDO", "Undo"),
        new("CONFIRM", "CONFirm"),
        new("CANCEL", "CANcel"),
        new("FIT", "Fit"),
        new("FITGROUND", "GroundFit", "GF", "FITG")
    ];

    private static readonly Keyword[] ViewKeywords =
    [
        new("FRONT", "Front"),
        new("BACK", "BAck"),
        new("LEFT", "Left"),
        new("RIGHT", "Right"),
        new("TOP", "Top"),
        new("BOTTOM", "Bottom", "BO", "BOT"),
        new("ISOMETRIC", "Isometric", "ISO")
    ];

    private static readonly Keyword[] ProjectionKeywords =
    [
        new("PERSPECTIVE", "Perspective"),
        new("PARALLEL", "PArallel", "ORTHO", "ORTHOGRAPHIC")
    ];

    private static readonly Keyword[] ColorKeywords =
    [
        new("RGB", "Rgb"),
        new("HEIGHT", "Height", "Z"),
        new("CLASS", "Class"),
        new("INTENSITY", "Intensity"),
        new("RETURN", "ReTurn"),
        new("CLEAR", "CLear")
    ];

    [CommandMethod("SELECT", "SEL")]
    public CommandResult Select(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();

        // Sync phase with authoritative controller state: if tool was cancelled externally
        // (e.g. UNDO, Escape key) while we thought we were in phase 2, reset.
        if (_selectPhase == 2 && !viewer.HasActiveSelection)
            _selectPhase = 0;

        string token = Token(context, SelectKeywords);
        bool empty = context.Keyword.Length == 0 && context.Input.Trim().Length == 0;

        if (empty)
        {
            if (viewer.HasActiveSelection)
            {
                viewer.ConfirmActiveSelection();
                _selectPhase = 0;
                return CommandResult.End("Selection applied.");
            }

            if (_selectPhase == 2)
                return CommandResult.Continue(SelectAdjustPrompt());

            if (_selectPhase == 1)
            {
                _selectPhase = 0;
                return StartTool(context, SelectionToolType.Box);
            }

            _selectPhase = 1;
            return CommandResult.Continue(SelectShapePrompt());
        }

        _selectPhase = 0;
        switch (token)
        {
            case "BOX":
                return StartTool(context, SelectionToolType.Box);
            case "SPHERE":
                return StartTool(context, SelectionToolType.Sphere);
            case "CYLINDER":
                return StartTool(context, SelectionToolType.Cylinder);
            case "UNDO":
                return CommandResult.Continue(
                    SelectAdjustPrompt(),
                    viewer.UndoSelectionCommand() ? "Last selection change undone." : "Nothing to undo.");
            case "CONFIRM":
                if (!viewer.HasActiveSelection)
                    return CommandResult.Continue(SelectAdjustPrompt(), "No active selection.");
                viewer.ConfirmActiveSelection();
                return CommandResult.End("Selection applied.");
            case "CANCEL":
                viewer.CancelOrExitLabelMode();
                return CommandResult.Cancel();
            case "FIT":
                _selectPhase = 2;
                return CommandResult.Continue(SelectAdjustPrompt(), viewer.FitActiveSelection(useGround: false));
            case "FITGROUND":
                _selectPhase = 2;
                return CommandResult.Continue(SelectAdjustPrompt(), viewer.FitActiveSelection(useGround: true));
            default:
                _selectPhase = 1;
                return CommandResult.Continue(SelectShapePrompt(), $"Invalid option keyword: {context.Input}");
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
        if (!TryParseLabelAnnotation(context.Input, out string name, out int? instanceId, out bool instanceSpecified, out string error))
        {
            string prompt = "Enter label name or <name> <instance id>:";
            return CommandResult.Continue(Value(prompt), error);
        }

        var viewer = context.GetTarget<ViewerController>();
        return CommandResult.End(instanceSpecified
            ? viewer.SetActiveAnnotation(name, instanceId)
            : viewer.SetActiveLabel(name));
    }

    [CommandMethod("INSTANCE", "INST", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Instance(CommandContext context)
    {
        string input = context.Input.Trim();
        if (input.Length == 0)
            return CommandResult.Continue(Value("Enter instance id or CLear:"));

        if (input.Equals("CLEAR", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("CL", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return CommandResult.End(context.GetTarget<ViewerController>().SetActiveInstance(null));

        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int instanceId) || instanceId < 0)
            return CommandResult.Continue(Value("Enter instance id or CLear:"), $"Invalid instance id: {context.Input}");

        return CommandResult.End(context.GetTarget<ViewerController>().SetActiveInstance(instanceId));
    }

    [CommandMethod("LABELDEF", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult LabelDef(CommandContext context)
    {
        string input = context.Input.Trim();
        if (input.Length == 0)
            return CommandResult.Continue(Value("Enter label definition: <name> <class code 0-255>:"));

        int split = input.LastIndexOf(' ');
        if (split <= 0 ||
            !byte.TryParse(input[(split + 1)..].Trim(), out byte code))
            return CommandResult.Continue(Value("Enter label definition: <name> <class code 0-255>:"),
                $"Expected '<name> <class code>'. Got: {context.Input}");

        string name = CommandText.Unquote(input[..split].Trim());
        if (name.Length == 0)
            return CommandResult.Continue(Value("Enter label definition: <name> <class code 0-255>:"), "Label name must not be empty.");

        return CommandResult.End(context.GetTarget<ViewerController>().DefineLabel(name, code));
    }

    [CommandMethod("LABELS", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult LabelsWindow(CommandContext context)
    {
        context.GetTarget<ViewerController>().ToggleLabelWindow();
        return CommandResult.End("Label registry window toggled.");
    }

    [CommandMethod("UNDO", "U", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Undo(CommandContext context) =>
        CommandResult.End(context.GetTarget<ViewerController>().UndoSelectionCommand() ? "Undo complete." : "Nothing to undo.");

    [CommandMethod("OPEN", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Open(CommandContext context)
    {
        if (!TryParseOpenArguments(context.Input, out string path, out long maxPoints, out string error))
            return CommandResult.Continue(Value("Enter LAS file path:"), error);

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
                return CommandResult.Continue(Value("Specify opposite corner:"));
            }

            return CommandResult.Continue(Value("Specify first corner:"), $"Invalid point: {context.Input}. Use x,y.");
        }

        if (_zoomPhase == ZoomPhase.OppositeCorner)
        {
            if (TryParseScreenPoint(option, out ScreenPoint oppositeCorner))
            {
                viewer.ZoomWindow(_zoomFirstCorner.X, _zoomFirstCorner.Y, oppositeCorner.X, oppositeCorner.Y);
                _zoomPhase = ZoomPhase.None;
                return CommandResult.End("Zoom window.");
            }

            return CommandResult.Continue(Value("Specify opposite corner:"), $"Invalid point: {context.Input}. Use x,y.");
        }

        if (option.Length == 0)
            return CommandResult.Continue(ZoomMainPrompt());

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
                return CommandResult.Continue(Value("Specify first corner:"));
        }

        if (!TryParseZoomFactor(option, out float factor, out string factorError))
            return CommandResult.Continue(ZoomMainPrompt(), factorError);

        viewer.ZoomByFactor(factor);
        return CommandResult.End($"Zoom scale factor: {factor.ToString("0.###", CultureInfo.InvariantCulture)}x");
    }

    [CommandMethod("VIEW", "V", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult View(CommandContext context)
    {
        string token = Token(context, ViewKeywords);
        if (token.Length == 0)
        {
            if (context.Input.Trim().Length == 0)
                return CommandResult.Continue(ViewPrompt());
            return CommandResult.Continue(ViewPrompt(), $"Invalid view keyword: {context.Input}");
        }

        context.GetTarget<ViewerController>().SetView(token);
        return CommandResult.End($"View: {token}");
    }

    [CommandMethod("PROJECTION", "PROJ", "PERSPECTIVE", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Projection(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string token = Token(context, ProjectionKeywords);

        switch (token)
        {
            case "PERSPECTIVE":
                return CommandResult.End(viewer.SetProjection(perspective: true));
            case "PARALLEL":
                return CommandResult.End(viewer.SetProjection(perspective: false));
        }

        // No keyword: empty input toggles; anything else is an invalid option.
        if (context.Input.Trim().Length == 0)
            return CommandResult.End(viewer.ToggleProjection());

        return CommandResult.Continue(ProjectionPrompt(), $"Invalid option keyword: {context.Input}");
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
            return CommandResult.Continue(Value($"Enter point size <{viewer.PointSize:0.0}>:"));

        float size;
        if (input == "+")
            size = viewer.PointSize + 0.5f;
        else if (input == "-")
            size = viewer.PointSize - 0.5f;
        else if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            return CommandResult.Continue(Value("Enter point size:"), $"Invalid point size: {input}");

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
            return CommandResult.Continue(Value(GetFilterValuePrompt(FilterAttribute.Class)));
        }

        if (input.Length == 0)
        {
            _filterPhase = FilterPhase.Attribute;
            return CommandResult.Continue(Value("Select filter attribute [Class/Intensity/Return/Z/CLear] <Class>:"));
        }

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!TryParseFilterAttribute(parts[0], out FilterAttribute attribute, out string attributeError))
            return CommandResult.Continue(Value("Select filter attribute [Class/Intensity/Return/Z/CLear] <Class>:"), attributeError);

        if (attribute == FilterAttribute.Clear)
        {
            _filterPhase = FilterPhase.None;
            return CommandResult.End(viewer.ApplyPointFilter(null));
        }

        if (parts.Length == 1)
        {
            _filterPhase = FilterPhase.Values;
            _pendingFilterAttribute = attribute;
            return CommandResult.Continue(Value(GetFilterValuePrompt(attribute)));
        }

        return ApplyParsedFilter(viewer, attribute, parts[1..]);
    }

    [CommandMethod("COLORBY", "COLOR", "CB", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult ColorBy(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string token = Token(context, ColorKeywords);
        if (token.Length == 0)
        {
            if (context.Input.Trim().Length == 0)
                return CommandResult.Continue(ColorPrompt());
            return CommandResult.Continue(ColorPrompt(), $"Invalid color source: {context.Input}");
        }

        if (token == "CLEAR")
            return CommandResult.End(viewer.ClearColorSource());

        return CommandResult.End(viewer.SetColorSource(MapColorSource(token)));
    }

    [CommandMethod("VPORTS", "VP", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Viewports(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string input = context.Input.Trim();

        if (_vportsViewPrompt)
        {
            if (input.Length == 0)
                input = ViewerController.DescribeViewportView(_lastViewportView);

            if (!TryParseViewportView(input, out ViewportViewKind view, out string viewError))
                return CommandResult.Continue(Value(GetViewportViewPrompt()), viewError);

            _vportsViewPrompt = false;
            _lastViewportView = view;
            if (_pendingViewportLayout != ViewportLayoutKind.Previous)
                _lastViewportLayout = _pendingViewportLayout;
            return CommandResult.End(viewer.SetViewportLayout(_pendingViewportLayout, view));
        }

        if (_vportsLayoutPrompt && input.Length == 0)
        {
            _vportsLayoutPrompt = false;
            _pendingViewportLayout = _lastViewportLayout;
            if (RequiresViewportArrangement(_pendingViewportLayout))
            {
                _vportsArrangementPrompt = true;
                return CommandResult.Continue(Value(GetViewportArrangementPrompt()));
            }

            return PromptForViewportView();
        }

        if (_vportsArrangementPrompt)
        {
            if (input.Length == 0)
                input = _lastViewportArrangement == ViewportLayoutKind.TwoHorizontal ? "H" : "V";

            _vportsArrangementPrompt = false;
            _vportsLayoutPrompt = false;
            if (!TryParseViewportArrangement(input, out ViewportLayoutKind arrangement, out string arrangementError))
                return CommandResult.Continue(Value(GetViewportArrangementPrompt()), arrangementError);

            _pendingViewportLayout = arrangement;
            _lastViewportArrangement = arrangement;
            return PromptForViewportView();
        }

        if (input.Length == 0)
        {
            _vportsLayoutPrompt = true;
            return CommandResult.Continue(Value(GetViewportLayoutPrompt()));
        }

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool wasLayoutPrompt = _vportsLayoutPrompt;
        if (!TryParseViewportLayout(parts[0], out ViewportLayoutKind layout, out bool requiresArrangement, out string error))
            return CommandResult.Continue(Value(GetViewportLayoutPrompt()), error);

        _vportsLayoutPrompt = false;
        if (requiresArrangement)
        {
            if (parts.Length > 1)
            {
                if (!TryParseViewportArrangement(parts[1], out layout, out string arrangementError))
                    return CommandResult.Continue(Value(GetViewportArrangementPrompt()), arrangementError);

                _pendingViewportLayout = layout;
                _lastViewportArrangement = layout;
                return PromptForViewportView();
            }

            if (!wasLayoutPrompt)
            {
                _pendingViewportLayout = _lastViewportArrangement;
                return PromptForViewportView();
            }

            _vportsArrangementPrompt = true;
            return CommandResult.Continue(Value(GetViewportArrangementPrompt()));
        }

        _pendingViewportLayout = layout;
        return PromptForViewportView();
    }

    [CommandMethod("SAVELABELS", "QLSAVE", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult SaveLabels(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string option = context.Input.Trim().ToUpperInvariant();
        if (option is "LAS" or "L")
            return CommandResult.End(viewer.SaveLabelsToLas());

        return CommandResult.End(viewer.SaveLabels() ? "Labels saved." : "No source file is associated with the viewer.");
    }

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

    [CommandMethod("FIT", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Fit(CommandContext context)
    {
        bool ground = context.Input.Trim().ToUpperInvariant() is "G" or "GROUND";
        return CommandResult.End(context.GetTarget<ViewerController>().FitActiveSelection(ground));
    }

    [CommandMethod("FITGROUND", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult FitGround(CommandContext context) =>
        CommandResult.End(context.GetTarget<ViewerController>().FitActiveSelection(useGround: true));

    [CommandMethod("HELP", "?", Flags = CommandFlags.NoUndoMarker | CommandFlags.NoHistory | CommandFlags.Transparent)]
    public CommandResult Help(CommandContext context) => CommandResult.End(
        "Commands: OPEN, SELECT, FILTER, COLORBY, ATTRS, VPORTS, ZOOM, VIEW, PROJECTION, POINTSIZE, LABEL, INSTANCE, SAVELABELS, LOADLABELS, CLEARLABELS, NAVIGATE, RESET, UNDO, HELP.");

    public void CancelCommand(CommandContext context, string globalCommandName)
    {
        switch (globalCommandName)
        {
            case "ZOOM":
                _zoomPhase = ZoomPhase.None;
                return;
            case "FILTER":
                _filterPhase = FilterPhase.None;
                return;
            case "VPORTS":
                _vportsLayoutPrompt = false;
                _vportsArrangementPrompt = false;
                _vportsViewPrompt = false;
                return;
            case "SELECT":
                context.GetTarget<ViewerController>().CancelOrExitLabelMode();
                _selectPhase = 0;
                return;
        }
    }

    // ----- Prompt builders -----

    private static PromptOptions Value(string message) => new(message) { AllowArbitraryInput = true };

    private static PromptOptions SelectShapePrompt() =>
        new("Specify selection shape [Box/Sphere/Cylinder/Undo] <Box>:", SelectKeywords) { DefaultKeyword = "BOX" };

    private static PromptOptions SelectAdjustPrompt() =>
        new("Adjust selection using grips or [CONFirm/Undo/CANcel]:", SelectKeywords);

    private static PromptOptions ViewPrompt() =>
        new("Enter view [Front/BAck/Left/Right/Top/Bottom/Isometric]:", ViewKeywords);

    private static PromptOptions ColorPrompt() =>
        new("Select color source [Rgb/Height/Class/Intensity/ReTurn/CLear] <Rgb>:", ColorKeywords) { DefaultKeyword = "RGB" };

    private static PromptOptions ProjectionPrompt() =>
        new("Enter projection [Perspective/PArallel] <toggle>:", ProjectionKeywords);

    private static PromptOptions ZoomMainPrompt() => Value(
        "Specify corner of window, enter a scale factor (nX or nXP), or [All/Center/Extents/Object] <real time>:");

    // Resolves the effective keyword: the runtime-resolved keyword for a prompt response,
    // or the first word of the inline argument on the initial command invocation.
    private static string Token(CommandContext context, Keyword[] keywords)
    {
        if (context.Keyword.Length > 0)
            return context.Keyword;

        string first = CommandText.FirstWord(context.Input);
        if (first.Length == 0)
            return "";

        foreach (Keyword keyword in keywords)
            if (keyword.Matches(first))
                return keyword.GlobalName;

        return "";
    }

    private static ColorSource MapColorSource(string globalKeyword) => globalKeyword switch
    {
        "HEIGHT" => ColorSource.Height,
        "CLASS" => ColorSource.Class,
        "INTENSITY" => ColorSource.Intensity,
        "RETURN" => ColorSource.Return,
        _ => ColorSource.Rgb
    };

    private CommandResult StartTool(CommandContext context, SelectionToolType tool)
    {
        context.GetTarget<ViewerController>().SetTool(tool);
        _selectPhase = 2;
        return CommandResult.Continue(
            SelectAdjustPrompt(),
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
            return CommandResult.Continue(Value("Select filter attribute [Class/Intensity/Return/Z/CLear] <Class>:"));
        }

        if (IsList(input))
            return CommandResult.Continue(Value(GetFilterValuePrompt(_pendingFilterAttribute)), viewer.DescribeAttributes(FilterAttributeToCommandName(_pendingFilterAttribute)));

        _filterPhase = FilterPhase.None;
        return ApplyParsedFilter(viewer, _pendingFilterAttribute, input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private CommandResult ApplyParsedFilter(ViewerController viewer, FilterAttribute attribute, string[] values)
    {
        if (!TryCreateFilter(attribute, values, out PointFilter? filter, out string error))
        {
            _filterPhase = FilterPhase.Values;
            _pendingFilterAttribute = attribute;
            return CommandResult.Continue(Value(GetFilterValuePrompt(attribute)), error);
        }

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

    private static bool TryParseLabelAnnotation(string input, out string name, out int? instanceId, out bool instanceSpecified, out string error)
    {
        name = "";
        instanceId = null;
        instanceSpecified = false;
        error = "";

        string trimmed = input.Trim();
        if (trimmed.Length == 0)
            return false;

        if (trimmed[0] == '"')
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote < 0)
            {
                error = "Missing closing quote in label name.";
                return false;
            }

            name = trimmed[1..endQuote].Trim();
            string rest = trimmed[(endQuote + 1)..].Trim();
            if (rest.Length > 0)
            {
                instanceSpecified = true;
                if (!TryParseInstanceToken(rest, out instanceId))
                {
                    error = $"Invalid instance id: {rest}";
                    return false;
                }
            }
        }
        else
        {
            int split = trimmed.LastIndexOf(' ');
            if (split > 0 && TryParseInstanceToken(trimmed[(split + 1)..].Trim(), out instanceId))
            {
                instanceSpecified = true;
                name = trimmed[..split].Trim();
            }
            else
                name = trimmed;
        }

        name = CommandText.Unquote(name);
        if (name.Length > 0)
            return true;

        error = "Label name must not be empty.";
        return false;
    }

    private static bool TryParseInstanceToken(string token, out int? instanceId)
    {
        instanceId = null;
        if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
            return false;

        instanceId = parsed;
        return true;
    }

    private static string FilterAttributeToCommandName(FilterAttribute attribute) => attribute switch
    {
        FilterAttribute.Class => "Class",
        FilterAttribute.Intensity => "Intensity",
        FilterAttribute.Return => "Return",
        FilterAttribute.Z => "Z",
        _ => "All"
    };

    private static bool TryParseViewportLayout(string input, out ViewportLayoutKind layout, out bool requiresArrangement, out string error)
    {
        requiresArrangement = false;
        layout = input.ToUpperInvariant() switch
        {
            "" or "S" or "SINGLE" => ViewportLayoutKind.SinglePerspective,
            "T" or "TWO" or "2" => ViewportLayoutKind.TwoVertical,
            "V" or "VERTICAL" => ViewportLayoutKind.TwoVertical,
            "H" or "HORIZONTAL" => ViewportLayoutKind.TwoHorizontal,
            "TOP" or "P" or "PLAN" => ViewportLayoutKind.SingleTop,
            "PR" or "PREVIOUS" => ViewportLayoutKind.Previous,
            _ => ViewportLayoutKind.SinglePerspective
        };

        requiresArrangement = input.Equals("T", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("TWO", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("2", StringComparison.OrdinalIgnoreCase);

        error = layout == ViewportLayoutKind.SinglePerspective &&
            !input.Equals("", StringComparison.Ordinal) &&
            !input.Equals("S", StringComparison.OrdinalIgnoreCase) &&
            !input.Equals("SINGLE", StringComparison.OrdinalIgnoreCase)
            ? $"Invalid viewport layout: {input}"
            : "";
        return error.Length == 0;
    }

    private static bool TryParseViewportArrangement(string input, out ViewportLayoutKind layout, out string error)
    {
        layout = input.ToUpperInvariant() switch
        {
            "" or "V" or "VERTICAL" => ViewportLayoutKind.TwoVertical,
            "H" or "HORIZONTAL" => ViewportLayoutKind.TwoHorizontal,
            _ => ViewportLayoutKind.SinglePerspective
        };

        error = layout == ViewportLayoutKind.SinglePerspective ? $"Invalid viewport arrangement: {input}" : "";
        return error.Length == 0;
    }

    private string GetViewportViewPrompt() =>
        $"Enter view [Top/Bottom/Left/Right/Front/BAck/Isometric] <{ViewerController.DescribeViewportView(_lastViewportView)}>:";

    private CommandResult PromptForViewportView()
    {
        _vportsViewPrompt = true;
        return CommandResult.Continue(Value(GetViewportViewPrompt()));
    }

    private string GetViewportLayoutPrompt() =>
        $"Enter viewport layout [Single/Two/Plan/Previous] <{DescribeViewportLayoutOption(_lastViewportLayout)}>:";

    private string GetViewportArrangementPrompt() =>
        $"Enter two viewport arrangement [Vertical/Horizontal] <{DescribeViewportArrangementOption(_lastViewportArrangement)}>:";

    private static bool RequiresViewportArrangement(ViewportLayoutKind layout) =>
        layout is ViewportLayoutKind.TwoVertical or ViewportLayoutKind.TwoHorizontal;

    private static string DescribeViewportLayoutOption(ViewportLayoutKind layout) => layout switch
    {
        ViewportLayoutKind.SingleTop => "Plan",
        ViewportLayoutKind.TwoVertical or ViewportLayoutKind.TwoHorizontal => "Two",
        ViewportLayoutKind.Previous => "Previous",
        _ => "Single"
    };

    private static string DescribeViewportArrangementOption(ViewportLayoutKind layout) =>
        layout == ViewportLayoutKind.TwoHorizontal ? "Horizontal" : "Vertical";

    private static bool TryParseViewportView(string input, out ViewportViewKind view, out string error)
    {
        view = input.ToUpperInvariant() switch
        {
            "" or "T" or "TOP" => ViewportViewKind.Top,
            "B" or "BO" or "BOT" or "BOTTOM" => ViewportViewKind.Bottom,
            "L" or "LEFT" => ViewportViewKind.Left,
            "R" or "RIGHT" => ViewportViewKind.Right,
            "F" or "FRONT" => ViewportViewKind.Front,
            "BA" or "BACK" => ViewportViewKind.Back,
            "I" or "ISO" or "ISOMETRIC" => ViewportViewKind.Isometric,
            _ => ViewportViewKind.Top
        };

        error = view == ViewportViewKind.Top &&
            !input.Equals("", StringComparison.Ordinal) &&
            !input.Equals("T", StringComparison.OrdinalIgnoreCase) &&
            !input.Equals("TOP", StringComparison.OrdinalIgnoreCase)
            ? $"Invalid viewport view: {input}"
            : "";
        return error.Length == 0;
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
    private enum FilterPhase { None, Attribute, Values }
    private enum FilterAttribute { Unknown, Class, Intensity, Return, Z, Clear }

    private readonly record struct ScreenPoint(int X, int Y);
}
