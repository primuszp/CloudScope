using CloudScope.Commands;
using CloudScope.Loading;
using OpenTK.Mathematics;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    [CommandMethod("ZOOM", "Z", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Zooms the view by a scale factor, to extents, or into a window.",
        Syntax = "ZOOM [All/Center/Extents/Object/Window] | <scale> | <nX> | <corner> <corner>")]
    public IEnumerable<PromptStep> Zoom(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStringStep option = ed
            .GetString("Specify corner of window, enter a scale factor (nX or nXP), or "
                     + "[All/Center/Extents/Object/Window] <real time>:")
            .WithKeywords(ZoomKeywords);
        yield return option;

        switch (option.Keyword)
        {
            case "ALL":
            case "EXTENTS":
                viewer.ZoomExtents();
                ed.WriteMessage("Zoom extents.");
                yield break;

            case "CENTER":
            case "OBJECT":
                viewer.ZoomCenter();
                ed.WriteMessage("Zoomed to object at viewport center.");
                yield break;

            case "WINDOW":
                foreach (PromptStep step in ZoomWindow(viewer, ed, first: null))
                    yield return step;
                yield break;
        }

        if (!option.IsOk) yield break;

        // A typed corner starts a window without the keyword, the way AutoCAD lets a point
        // answer the main ZOOM prompt.
        if (TryParseScreenPoint(option.Value, out int x, out int y))
        {
            foreach (PromptStep step in ZoomWindow(viewer, ed, (x, y)))
                yield return step;
            yield break;
        }

        if (!TryParseZoomFactor(option.Value, out float factor))
        {
            ed.WriteMessage($"Requires a positive numeric scale factor: {option.Value}");
            yield break;
        }

        viewer.ZoomByFactor(factor);
        ed.WriteMessage($"Zoom scale factor: {factor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}x");
    }

    private static IEnumerable<PromptStep> ZoomWindow(ViewerController viewer, Editor ed, (int X, int Y)? first)
    {
        int x0, y0;
        if (first is { } corner)
        {
            (x0, y0) = corner;
        }
        else
        {
            PromptScreenPointStep start = ed.GetScreenPoint("Specify first corner:");
            yield return start;
            if (!start.IsOk) yield break;
            (x0, y0) = (start.X, start.Y);
        }

        PromptScreenPointStep opposite = ed.GetScreenPoint("Specify opposite corner:");
        yield return opposite;
        if (!opposite.IsOk) yield break;

        viewer.ZoomWindow(x0, y0, opposite.X, opposite.Y);
        ed.WriteMessage("Zoom window.");
    }

    [CommandMethod("PAN", "P", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Pans the view by a pixel offset or between two points.",
        Syntax = "PAN <dx,dy> | Point <base> <target>")]
    public IEnumerable<PromptStep> Pan(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptScreenPointStep offset = ed
            .GetScreenPoint("Specify pan offset in pixels dx,dy or [Point]:")
            .WithKeywords(new Keyword("POINT", "Point"));
        yield return offset;

        if (offset.Is("POINT"))
        {
            PromptScreenPointStep from = ed.GetScreenPoint("Specify base point:");
            yield return from;
            if (!from.IsOk) yield break;

            PromptScreenPointStep to = ed.GetScreenPoint("Specify target point:");
            yield return to;
            if (!to.IsOk) yield break;

            ed.WriteMessage(viewer.PanByPixels(to.X - from.X, to.Y - from.Y));
            yield break;
        }

        if (!offset.IsOk) yield break;
        ed.WriteMessage(viewer.PanByPixels(offset.X, offset.Y));
    }

    [CommandMethod("ORBIT", "OR", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Orbits the camera by an angle, or resets the orbit.",
        Syntax = "ORBIT <azimuth,elevation> | Reset")]
    public IEnumerable<PromptStep> Orbit(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStringStep angles = ed
            .GetString("Specify orbit as azimuth,elevation in degrees or [Reset]:")
            .WithKeywords(new Keyword("RESET", "Reset"));
        yield return angles;

        if (angles.Is("RESET"))
        {
            viewer.SetView("ISOMETRIC");
            ed.WriteMessage("Orbit reset to isometric.");
            yield break;
        }

        if (!angles.IsOk) yield break;

        if (!TryParsePair(angles.Value, out double azimuth, out double elevation))
        {
            ed.WriteMessage($"Invalid orbit: {angles.Value}. Use azimuth,elevation in degrees.");
            yield break;
        }

        ed.WriteMessage(viewer.OrbitBy((float)azimuth, (float)elevation));
    }

    [CommandMethod("PIVOT", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Sets the point the view orbits around.",
        Syntax = "PIVOT <x,y,z> | Screen <x,y> | Extents")]
    public IEnumerable<PromptStep> Pivot(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptPointStep point = ed
            .GetPoint("Specify pivot point or [Screen/Extents]:")
            .WithKeywords(new Keyword("SCREEN", "Screen"), new Keyword("EXTENTS", "Extents"));
        yield return point;

        if (point.Is("EXTENTS"))
        {
            ed.WriteMessage(viewer.ResetOrbitPivot());
            yield break;
        }

        if (point.Is("SCREEN"))
        {
            PromptScreenPointStep screen = ed.GetScreenPoint("Specify screen position x,y:");
            yield return screen;
            if (!screen.IsOk) yield break;

            ed.WriteMessage(viewer.SetOrbitPivotFromScreen(screen.X, screen.Y));
            yield break;
        }

        if (!point.IsOk) yield break;
        ed.WriteMessage(viewer.SetOrbitPivot(point.Value));
    }

    [CommandMethod("VIEW", "V", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Sets a standard view, or saves and restores a named one.",
        Syntax = "VIEW [Front/BAck/Left/Right/Top/Bottom/Isometric/Save/Restore/LIst/DElete]")]
    public IEnumerable<PromptStep> View(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStep option = ed.GetKeywords(
            "Enter view [Front/BAck/Left/Right/Top/Bottom/Isometric/Save/Restore/LIst/DElete]:",
            ViewKeywords);
        yield return option;

        switch (option.Keyword)
        {
            case "LIST":
                ed.WriteMessage(viewer.DescribeNamedViews());
                yield break;

            case "SAVE":
            case "RESTORE":
            case "DELETE":
                PromptStringStep name = ed.GetLine($"Enter view name to {option.Keyword.ToLowerInvariant()}:");
                yield return name;
                if (!name.IsOk) yield break;

                ed.WriteMessage(option.Keyword switch
                {
                    "SAVE" => viewer.SaveNamedView(name.Value),
                    "RESTORE" => viewer.RestoreNamedView(name.Value),
                    _ => viewer.DeleteNamedView(name.Value)
                });
                yield break;

            case "":
                yield break;

            default:
                viewer.SetView(option.Keyword);
                ed.WriteMessage($"View: {option.Keyword}");
                yield break;
        }
    }

    [CommandMethod("PROJECTION", "PROJ", "PERSPECTIVE", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Switches between perspective and parallel projection.",
        Syntax = "PROJECTION [Perspective/PArallel]")]
    public IEnumerable<PromptStep> Projection(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        PromptStep option = context.Editor
            .GetKeywords("Enter projection [Perspective/PArallel] <toggle>:", ProjectionKeywords);
        yield return option;

        context.Editor.WriteMessage(option.Keyword switch
        {
            "PERSPECTIVE" => viewer.SetProjection(perspective: true),
            "PARALLEL" => viewer.SetProjection(perspective: false),
            _ => viewer.ToggleProjection()
        });
    }

    [CommandMethod("RESET", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Resets the viewer to its initial view and state.",
        Syntax = "RESET")]
    public IEnumerable<PromptStep> Reset(CommandContext context)
    {
        context.GetTarget<ViewerController>().Reset();
        context.Editor.WriteMessage("Viewer reset.");
        yield break;
    }

    [CommandMethod("POINTSIZE", "PSIZE", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Sets the on-screen size of a point, in pixels.",
        Syntax = "POINTSIZE <size> | + | -")]
    public IEnumerable<PromptStep> PointSize(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        PromptDoubleStep size = context.Editor
            .GetDouble($"Enter point size <{viewer.PointSize:0.0}>:")
            .AllowingRelative(viewer.PointSize)
            .WithRange(0.5, 20);
        yield return size;
        if (!size.IsOk) yield break;

        viewer.SetPointSize(size.Single);
        context.Editor.WriteMessage($"Point size: {viewer.PointSize:0.0}");
    }

    [CommandMethod("COLORBY", "COLOR", "CB", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Document,
        Summary = "Colours the cloud by one of its attributes.",
        Syntax = "COLORBY [Rgb/Height/Class/Intensity/ReTurn/CLear]")]
    public IEnumerable<PromptStep> ColorBy(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        PromptStep source = context.Editor
            .GetKeywords("Select color source [Rgb/Height/Class/Intensity/ReTurn/CLear] <Rgb>:", ColorKeywords)
            .WithDefaultKeyword("RGB");
        yield return source;

        if (source.Status != PromptStatus.Keyword) yield break;

        context.Editor.WriteMessage(source.Is("CLEAR")
            ? viewer.ClearColorSource()
            : viewer.SetColorSource(MapColorSource(source.Keyword)));
    }

    [CommandMethod("FILTER", "FI", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Document,
        Summary = "Shows only the points matching an attribute filter.",
        Syntax = "FILTER [Class/Intensity/Return/Z/CLear] <values>")]
    public IEnumerable<PromptStep> Filter(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        while (true)
        {
            PromptStep attribute = ed
                .GetKeywords("Select filter attribute [Class/Intensity/Return/Z/CLear] <Class>:", FilterKeywords)
                .WithDefaultKeyword("CLASS");
            yield return attribute;

            if (attribute.Status != PromptStatus.Keyword) yield break;

            if (attribute.Is("CLEAR"))
            {
                ed.WriteMessage(viewer.ApplyPointFilter(null));
                yield break;
            }

            bool back = false;
            while (!back)
            {
                PromptStringStep values = ed
                    .GetLine(FilterValuePrompt(attribute.Keyword))
                    .WithKeywords(FilterValueKeywords)
                    .WithDefaultKeyword(attribute.Is("CLASS") ? "LIST" : "");
                yield return values;

                if (values.Is("BACK"))
                {
                    back = true;
                    continue;
                }

                if (values.Is("LIST"))
                {
                    ed.WriteMessage(viewer.DescribeAttributes(FilterAttributeName(attribute.Keyword)));
                    continue;
                }

                if (!values.IsOk) yield break;

                if (!TryCreateFilter(attribute.Keyword, values.Value, out PointFilter? filter, out string error))
                {
                    ed.WriteMessage(error);
                    continue;
                }

                ed.WriteMessage(viewer.ApplyPointFilter(filter));
                yield break;
            }
        }
    }

    private static string FilterValuePrompt(string attribute) => attribute switch
    {
        "CLASS" => "Enter class values or [List/Back] <List>:",
        "INTENSITY" => "Enter intensity range min max or [List/Back]:",
        "RETURN" => "Enter return values or [List/Back]:",
        "Z" => "Enter Z range min max or [List/Back]:",
        _ => "Enter filter value:"
    };

    private static string FilterAttributeName(string attribute) => attribute switch
    {
        "CLASS" => "Class",
        "INTENSITY" => "Intensity",
        "RETURN" => "Return",
        "Z" => "Z",
        _ => "All"
    };

    private static bool TryCreateFilter(string attribute, string text, out PointFilter? filter, out string error)
    {
        filter = null;
        error = "";

        switch (attribute)
        {
            case "CLASS":
                if (!TryParseByteList(text, out byte[] classes))
                {
                    error = "Enter class values like 3 or 2,3,5.";
                    return false;
                }

                filter = new ClassFilter(classes);
                return true;

            case "RETURN":
                if (!TryParseByteList(text, out byte[] returns))
                {
                    error = "Enter return values like 1 or 1,2.";
                    return false;
                }

                filter = new ReturnFilter(returns);
                return true;

            case "INTENSITY":
                if (!TryParseRange(text, out double minIntensity, out double maxIntensity) ||
                    minIntensity < 0 || maxIntensity > ushort.MaxValue)
                {
                    error = "Enter intensity range as: min max.";
                    return false;
                }

                filter = new IntensityFilter((ushort)minIntensity, (ushort)maxIntensity);
                return true;

            case "Z":
                if (!TryParseRange(text, out double minZ, out double maxZ))
                {
                    error = "Enter Z range as: min max.";
                    return false;
                }

                filter = new ZFilter(minZ, maxZ);
                return true;

            default:
                error = "Unsupported filter attribute.";
                return false;
        }
    }

    [CommandMethod("VPORTS", "VP", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.View, Scope = CommandScope.Viewer,
        Summary = "Splits the drawing area into viewports.",
        Syntax = "VPORTS [Single/2/3/4/5/6/7/8/9/Plan/PRevious] [Vertical/Horizontal] [view]")]
    public IEnumerable<PromptStep> Viewports(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptStep layout = ed
            .GetKeywords(
                $"Enter viewport layout [Single/2/3/4/5/6/7/8/9/Plan/PRevious] <{DescribeLayout(_lastViewportLayout)}>:",
                ViewportLayoutKeywords)
            .WithDefaultKeyword(DescribeLayoutKeyword(_lastViewportLayout));
        yield return layout;
        if (layout.Status != PromptStatus.Keyword) yield break;

        ViewportLayoutKind kind;
        switch (layout.Keyword)
        {
            case "SINGLE":
                kind = ViewportLayoutKind.SinglePerspective;
                break;
            case "PLAN":
                kind = ViewportLayoutKind.SingleTop;
                break;
            case "PREVIOUS":
                kind = ViewportLayoutKind.Previous;
                break;
            case "TWO":
                PromptStep arrangement = ed
                    .GetKeywords(
                        $"Enter two viewport arrangement [Vertical/Horizontal] "
                        + $"<{(_lastViewportArrangement == ViewportLayoutKind.TwoHorizontal ? "Horizontal" : "Vertical")}>:",
                        ViewportArrangementKeywords)
                    .WithDefaultKeyword(
                        _lastViewportArrangement == ViewportLayoutKind.TwoHorizontal ? "HORIZONTAL" : "VERTICAL");
                yield return arrangement;
                if (arrangement.Status != PromptStatus.Keyword) yield break;

                kind = arrangement.Is("HORIZONTAL")
                    ? ViewportLayoutKind.TwoHorizontal
                    : ViewportLayoutKind.TwoVertical;
                _lastViewportArrangement = kind;
                break;
            case "THREE": kind = ViewportLayoutKind.Three; break;
            case "FOUR": kind = ViewportLayoutKind.Four; break;
            case "FIVE": kind = ViewportLayoutKind.Five; break;
            case "SIX": kind = ViewportLayoutKind.Six; break;
            case "SEVEN": kind = ViewportLayoutKind.Seven; break;
            case "EIGHT": kind = ViewportLayoutKind.Eight; break;
            case "NINE": kind = ViewportLayoutKind.Nine; break;
            default:
                yield break;
        }

        PromptStep view = ed
            .GetKeywords(
                $"Enter view [Top/Bottom/Left/Right/Front/BAck/Isometric] "
                + $"<{ViewerController.DescribeViewportView(_lastViewportView)}>:",
                ViewportViewKeywords)
            .WithDefaultKeyword(_lastViewportView.ToString().ToUpperInvariant());
        yield return view;
        if (view.Status != PromptStatus.Keyword) yield break;

        _lastViewportView = ParseViewportView(view.Keyword);
        if (kind != ViewportLayoutKind.Previous)
            _lastViewportLayout = kind;

        ed.WriteMessage(viewer.SetViewportLayout(kind, _lastViewportView));
    }

    private static ViewportViewKind ParseViewportView(string keyword) => keyword switch
    {
        "BOTTOM" => ViewportViewKind.Bottom,
        "LEFT" => ViewportViewKind.Left,
        "RIGHT" => ViewportViewKind.Right,
        "FRONT" => ViewportViewKind.Front,
        "BACK" => ViewportViewKind.Back,
        "ISOMETRIC" => ViewportViewKind.Isometric,
        _ => ViewportViewKind.Top
    };

    private static string DescribeLayout(ViewportLayoutKind layout) => layout switch
    {
        ViewportLayoutKind.SingleTop => "Plan",
        ViewportLayoutKind.TwoVertical or ViewportLayoutKind.TwoHorizontal => "Two",
        ViewportLayoutKind.Three => "Three",
        ViewportLayoutKind.Four => "Four",
        ViewportLayoutKind.Five => "Five",
        ViewportLayoutKind.Six => "Six",
        ViewportLayoutKind.Seven => "Seven",
        ViewportLayoutKind.Eight => "Eight",
        ViewportLayoutKind.Nine => "Nine",
        ViewportLayoutKind.Previous => "PRevious",
        _ => "Single"
    };

    private static string DescribeLayoutKeyword(ViewportLayoutKind layout) => layout switch
    {
        ViewportLayoutKind.SingleTop => "PLAN",
        ViewportLayoutKind.TwoVertical or ViewportLayoutKind.TwoHorizontal => "TWO",
        ViewportLayoutKind.Three => "THREE",
        ViewportLayoutKind.Four => "FOUR",
        ViewportLayoutKind.Five => "FIVE",
        ViewportLayoutKind.Six => "SIX",
        ViewportLayoutKind.Seven => "SEVEN",
        ViewportLayoutKind.Eight => "EIGHT",
        ViewportLayoutKind.Nine => "NINE",
        ViewportLayoutKind.Previous => "PREVIOUS",
        _ => "SINGLE"
    };
}
