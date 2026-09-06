using CloudScope.Commands;
using CloudScope.Drawing;
using OpenTK.Mathematics;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] PolylineKeywords =
    [
        new("CLOSE", "Close", "C"),
        new("UNDO", "Undo", "U")
    ];

    private static readonly Keyword[] PlineLineKeywords =
    [
        new("ARC", "Arc", "A"),
        new("CLOSE", "Close", "C"),
        new("HALFWIDTH", "Halfwidth", "H"),
        new("LENGTH", "Length", "L"),
        new("UNDO", "Undo", "U"),
        new("WIDTH", "Width", "W")
    ];

    private static readonly Keyword[] PlineArcKeywords =
    [
        new("ANGLE", "Angle", "A"),
        new("CENTER", "Center", "CE"),
        new("CLOSE", "Close", "CL"),
        new("DIRECTION", "Direction", "D"),
        new("HALFWIDTH", "Halfwidth", "H"),
        new("LINE", "Line", "L"),
        new("RADIUS", "Radius", "R"),
        new("SECOND", "Second pt", "S"),
        new("UNDO", "Undo", "U"),
        new("WIDTH", "Width", "W")
    ];

    [CommandMethod("PLINE", "PL",
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Draws a planar polyline made of connected line and tangent arc segments.",
        Syntax = "PLINE <start point> <next point>... [Arc/Close/Halfwidth/Length/Undo/Width]")]
    public IEnumerable<PromptStep> PlanarPolyline(CommandContext context)
    {
        ViewerController viewer = context.GetTarget<ViewerController>();
        Editor editor = context.Editor;

        PromptPointStep first = editor.GetPoint("Specify start point:");
        if (viewer.LastPlanarPolylineEndpoint is { } previousEndpoint)
            first.WithDefault(previousEndpoint);
        yield return first;
        if (!first.IsOk) yield break;

        viewer.BeginPlanarPolylineDraft(first.Value);
        bool committed = false;
        bool arcMode = false;
        float startWidth = 0f;
        float endWidth = 0f;
        try
        {
            while (true)
            {
                string prompt = arcMode
                    ? "Specify endpoint of arc or [Angle/Center/Close/Direction/Halfwidth/Line/Radius/Second pt/Undo/Width]:"
                    : "Specify next point or [Arc/Close/Halfwidth/Length/Undo/Width] <finish>:";
                PromptPointStep next = editor.GetCorner(prompt, viewer.PlanarPolylineDraftLastPoint)
                    .WithDirectionalDistance(distance => viewer.PlanarPolylineDraftLastPoint
                        + viewer.PlanarPolylineDraftDirection * (float)distance)
                    .WithKeywords(arcMode ? PlineArcKeywords : PlineLineKeywords);
                yield return next;

                if (next.Status == PromptStatus.None)
                {
                    if (viewer.PlanarPolylineDraftVertexCount < 2)
                    {
                        editor.WriteMessage("Specify at least one more point.");
                        continue;
                    }
                    editor.WriteMessage(viewer.CommitPlanarPolylineDraft(closed: false));
                    committed = true;
                    yield break;
                }

                if (next.Is("ARC"))
                {
                    arcMode = true;
                    viewer.SetPlanarPolylineArcMode(true);
                    continue;
                }
                if (next.Is("LINE"))
                {
                    arcMode = false;
                    viewer.SetPlanarPolylineArcMode(false);
                    continue;
                }
                if (next.Is("UNDO"))
                {
                    editor.WriteMessage(viewer.UndoPlanarPolylineDraftVertex()
                        ? "Last polyline segment removed."
                        : "The start point cannot be removed.");
                    continue;
                }
                if (next.Is("ANGLE"))
                {
                    PromptDoubleStep angle = editor.GetAngle("Specify included angle:");
                    yield return angle;
                    if (!angle.IsOk) yield break;
                    PromptPointStep endpoint = editor.GetCorner("Specify endpoint of arc:",
                        viewer.PlanarPolylineDraftLastPoint);
                    yield return endpoint;
                    if (!endpoint.IsOk) yield break;
                    viewer.AddPlanarPolylineDraftVertex(endpoint.Value,
                        PlanarPolylineGeometry.IncludedAngleBulge(angle.Single));
                    continue;
                }
                if (next.Is("CENTER"))
                {
                    Vector3 startPoint = viewer.PlanarPolylineDraftLastPoint;
                    PromptPointStep center = editor.GetPoint("Specify center point of arc:");
                    yield return center;
                    if (!center.IsOk) yield break;
                    PromptPointStep endpoint = editor.GetCorner("Specify endpoint of arc:", startPoint);
                    yield return endpoint;
                    if (!endpoint.IsOk) yield break;
                    Vector2 start2 = viewer.PlanarPolylineDraftPlanePoint(startPoint);
                    Vector2 center2 = viewer.PlanarPolylineDraftPlanePoint(center.Value);
                    Vector2 radial = start2 - center2;
                    Vector2 ccwTangent = new(-radial.Y, radial.X);
                    float sign = Vector2.Dot(ccwTangent, viewer.PlanarPolylineDraftTangent) >= 0f ? 1f : -1f;
                    viewer.AddPlanarPolylineDraftVertex(endpoint.Value,
                        PlanarPolylineGeometry.CenterArcBulge(start2,
                            viewer.PlanarPolylineDraftPlanePoint(endpoint.Value), center2, sign));
                    continue;
                }
                if (next.Is("DIRECTION"))
                {
                    Vector3 startPoint = viewer.PlanarPolylineDraftLastPoint;
                    PromptPointStep direction = editor.GetCorner(
                        "Specify tangent direction from start point of arc:", startPoint);
                    yield return direction;
                    if (!direction.IsOk) yield break;
                    PromptPointStep endpoint = editor.GetCorner("Specify endpoint of arc:", startPoint);
                    yield return endpoint;
                    if (!endpoint.IsOk) yield break;
                    Vector2 start2 = viewer.PlanarPolylineDraftPlanePoint(startPoint);
                    viewer.AddPlanarPolylineDraftVertex(endpoint.Value,
                        PlanarPolylineGeometry.TangentArcBulge(start2,
                            viewer.PlanarPolylineDraftPlanePoint(endpoint.Value),
                            viewer.PlanarPolylineDraftPlanePoint(direction.Value) - start2));
                    continue;
                }
                if (next.Is("RADIUS"))
                {
                    PromptDoubleStep radius = editor.GetDistance("Specify radius of arc:")
                        .WithRange(double.Epsilon, double.MaxValue);
                    yield return radius;
                    if (!radius.IsOk) yield break;
                    Vector3 startPoint = viewer.PlanarPolylineDraftLastPoint;
                    PromptPointStep endpoint = editor.GetCorner("Specify endpoint of arc:", startPoint);
                    yield return endpoint;
                    if (!endpoint.IsOk) yield break;
                    Vector2 start2 = viewer.PlanarPolylineDraftPlanePoint(startPoint);
                    float bulge = PlanarPolylineGeometry.RadiusArcBulge(start2,
                        viewer.PlanarPolylineDraftPlanePoint(endpoint.Value), radius.Single,
                        viewer.PlanarPolylineDraftTangent);
                    if (MathF.Abs(bulge) < 1e-7f)
                    {
                        editor.WriteMessage("The radius is too small for that endpoint.");
                        continue;
                    }
                    viewer.AddPlanarPolylineDraftVertex(endpoint.Value, bulge);
                    continue;
                }
                if (next.Is("SECOND"))
                {
                    Vector3 startPoint = viewer.PlanarPolylineDraftLastPoint;
                    PromptPointStep through = editor.GetCorner("Specify second point on arc:", startPoint);
                    yield return through;
                    if (!through.IsOk) yield break;
                    PromptPointStep endpoint = editor.GetCorner("Specify endpoint of arc:", startPoint);
                    yield return endpoint;
                    if (!endpoint.IsOk) yield break;
                    viewer.AddPlanarPolylineDraftVertex(endpoint.Value,
                        PlanarPolylineGeometry.ThreePointArcBulge(
                            viewer.PlanarPolylineDraftPlanePoint(startPoint),
                            viewer.PlanarPolylineDraftPlanePoint(through.Value),
                            viewer.PlanarPolylineDraftPlanePoint(endpoint.Value)));
                    continue;
                }
                if (next.Is("CLOSE"))
                {
                    if (viewer.PlanarPolylineDraftVertexCount < 3)
                    {
                        editor.WriteMessage("Specify at least three points before Close.");
                        continue;
                    }
                    editor.WriteMessage(viewer.CommitPlanarPolylineDraft(closed: true));
                    committed = true;
                    yield break;
                }
                if (next.Is("LENGTH"))
                {
                    PromptDoubleStep length = editor.GetDistance("Specify length of line:")
                        .WithRange(double.Epsilon, double.MaxValue);
                    yield return length;
                    if (!length.IsOk) yield break;
                    viewer.AddPlanarPolylineDraftVertex(viewer.PlanarPolylineDraftLastPoint
                        + viewer.PlanarPolylineDraftDirection * length.Single);
                    continue;
                }
                if (next.Is("WIDTH") || next.Is("HALFWIDTH"))
                {
                    bool half = next.Is("HALFWIDTH");
                    PromptDoubleStep start = editor.GetDistance(half
                            ? $"Specify starting half-width <{startWidth * 0.5f:0.###}>:"
                            : $"Specify starting width <{startWidth:0.###}>:")
                        .WithDefault(half ? startWidth * 0.5f : startWidth);
                    yield return start;
                    if (!start.IsOk) yield break;
                    PromptDoubleStep end = editor.GetDistance(half
                            ? $"Specify ending half-width <{start.Value:0.###}>:"
                            : $"Specify ending width <{start.Value:0.###}>:")
                        .WithDefault(start.Value);
                    yield return end;
                    if (!end.IsOk) yield break;
                    startWidth = (float)start.Value * (half ? 2f : 1f);
                    endWidth = (float)end.Value * (half ? 2f : 1f);
                    viewer.SetPlanarPolylineWidth(startWidth, endWidth);
                    continue;
                }

                if (!next.IsOk) yield break;
                viewer.AddPlanarPolylineDraftVertex(next.Value);
                startWidth = endWidth;
            }
        }
        finally
        {
            if (!committed) viewer.ClearPlanarPolylineDraft();
        }
    }

    [CommandMethod("PEDIT", "PE",
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Edits the selected planar polyline.",
        Syntax = "PEDIT [Close/Join/Open/Reverse/Width]")]
    public IEnumerable<PromptStep> EditPlanarPolyline(CommandContext context)
    {
        ViewerController viewer = context.GetTarget<ViewerController>();
        Editor editor = context.Editor;
        if (!viewer.HasSelectedPlanarPolyline)
        {
            editor.WriteMessage("Select a planar polyline first.");
            yield break;
        }

        PromptKeywordStep option = editor.GetKeywords(
            "Enter an option [Close/Join/Open/Reverse/Width] <exit>:",
            new Keyword("CLOSE", "Close", "C"),
            new Keyword("JOIN", "Join", "J"),
            new Keyword("OPEN", "Open", "O"),
            new Keyword("REVERSE", "Reverse", "R"),
            new Keyword("WIDTH", "Width", "W"));
        yield return option;
        if (option.Status == PromptStatus.None) yield break;

        if (option.Is("CLOSE"))
            editor.WriteMessage(viewer.SetSelectedPlanarPolylineClosed(true));
        else if (option.Is("OPEN"))
            editor.WriteMessage(viewer.SetSelectedPlanarPolylineClosed(false));
        else if (option.Is("JOIN"))
        {
            PromptDoubleStep tolerance = editor.GetDistance("Specify join tolerance <0.001>:")
                .WithDefault(0.001);
            yield return tolerance;
            if (tolerance.IsOk)
                editor.WriteMessage(viewer.JoinSelectedPlanarPolyline(tolerance.Single));
        }
        else if (option.Is("REVERSE"))
            editor.WriteMessage(viewer.ReverseSelectedPlanarPolyline());
        else if (option.Is("WIDTH"))
        {
            PromptDoubleStep width = editor.GetDistance("Specify new width:");
            yield return width;
            if (width.IsOk)
                editor.WriteMessage(viewer.SetSelectedPlanarPolylineWidth(width.Single));
        }
    }

    [CommandMethod("SAVEPOLYLINES",
        Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Saves every planar polyline to a versioned JSON document.",
        Syntax = "SAVEPOLYLINES <path>")]
    public IEnumerable<PromptStep> SavePlanarPolylines(CommandContext context)
    {
        PromptFileStep path = context.Editor.GetFileNameForSave("Polyline file name:");
        yield return path;
        if (path.IsOk)
            context.Editor.WriteMessage(context.GetTarget<ViewerController>()
                .SavePlanarPolylines(path.Value));
    }

    [CommandMethod("LOADPOLYLINES",
        Group = CommandGroup.File, Scope = CommandScope.Viewer,
        Summary = "Loads planar polylines from a versioned JSON document.",
        Syntax = "LOADPOLYLINES <path>")]
    public IEnumerable<PromptStep> LoadPlanarPolylines(CommandContext context)
    {
        PromptFileStep path = context.Editor.GetFileNameForOpen("Polyline file name:");
        yield return path;
        if (path.IsOk)
            context.Editor.WriteMessage(context.GetTarget<ViewerController>()
                .LoadPlanarPolylines(path.Value));
    }

    [CommandMethod("ORTHO",
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Turns axis locking for point input on or off.",
        Syntax = "ORTHO [ON/OFF] <toggle>")]
    public IEnumerable<PromptStep> Ortho(CommandContext context)
    {
        ViewerController viewer = context.GetTarget<ViewerController>();
        Editor editor = context.Editor;

        PromptKeywordStep option = editor.GetKeywords(
            $"Enter mode [ON/OFF] <{(viewer.OrthoMode ? "ON" : "OFF")}>:",
            new Keyword("ON", "ON"),
            new Keyword("OFF", "OFF"));
        yield return option;

        // A bare Enter toggles, which is how the status bar button and F8 behave.
        editor.WriteMessage(option.Status == PromptStatus.None
            ? viewer.ToggleOrthoMode()
            : viewer.SetOrthoMode(option.Is("ON")));
    }

    [CommandMethod("3DPOLY",
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Draws a straight-segment 3D polyline with object and axis snapping.",
        Syntax = "3DPOLY <first point> <next point>... [Close/Undo]")]
    public IEnumerable<PromptStep> Polyline3D(CommandContext context)
    {
        ViewerController viewer = context.GetTarget<ViewerController>();
        Editor editor = context.Editor;

        PromptPointStep first = editor.GetPoint("Specify start point:");
        yield return first;
        if (!first.IsOk) yield break;

        viewer.BeginPolylineDraft(first.Value);
        bool committed = false;
        try
        {
            while (true)
            {
                PromptPointStep next = editor
                    .GetCorner("Specify next point or [Close/Undo] <finish>:",
                        viewer.PolylineDraftLastPoint)
                    .WithDirectionalDistance(distance => viewer.PointAlongCurrentDirection(
                        viewer.PolylineDraftLastPoint, distance))
                    .WithKeywords(PolylineKeywords);
                yield return next;

                if (next.Status == PromptStatus.None)
                {
                    if (viewer.PolylineDraftVertexCount < 2)
                    {
                        editor.WriteMessage("Specify at least one more point.");
                        continue;
                    }
                    editor.WriteMessage(viewer.CommitPolylineDraft(closed: false));
                    committed = true;
                    yield break;
                }

                if (next.Is("CLOSE"))
                {
                    if (viewer.PolylineDraftVertexCount < 3)
                    {
                        editor.WriteMessage("Specify at least three points before Close.");
                        continue;
                    }
                    editor.WriteMessage(viewer.CommitPolylineDraft(closed: true));
                    committed = true;
                    yield break;
                }

                if (next.Is("UNDO"))
                {
                    editor.WriteMessage(viewer.UndoPolylineDraftVertex()
                        ? "Last polyline vertex removed."
                        : "The start point cannot be removed.");
                    continue;
                }

                if (!next.IsOk) yield break;
                viewer.AddPolylineDraftVertex(next.Value);
            }
        }
        finally
        {
            if (!committed) viewer.ClearPolylineDraft();
        }
    }
}
