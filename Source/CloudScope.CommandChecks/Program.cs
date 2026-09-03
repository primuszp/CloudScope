// Command-system checks. Run with:  dotnet run --project Source/CloudScope.CommandChecks
//
// These cover the two things that rot quietly: the command table (every command declared,
// described and reachable from the menu) and the runtime's conversation rules (prompts,
// keywords, defaults, cancellation, transparency, repeat). They need no display, so they
// run anywhere the solution builds.

using CloudScope.Commands;
using CloudScope;
using CloudScope.Ui.Commands;
using CloudScope.Sections;
using CloudScope.Selection;
using CloudScope.Drawing;
using CloudScope.Rendering;
using CloudScope.Forestry;
using OpenTK.Mathematics;

int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "  ok  " : "FAIL  ")}{name}{(ok || detail.Length == 0 ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}

// ---------- 1. Registration of the real command set ----------
try
{
    var runtime = new CommandRuntime(new object(), new ViewerCommands());
    var commands = runtime.Commands;
    Check("ViewerCommands registers", commands.Count > 0, "");
    Console.WriteLine($"       {commands.Count} commands, {runtime.KnownCommandNames.Count} names incl. aliases");

    Check("every command has a summary", commands.All(c => c.Summary.Length > 0));
    Check("every command has a syntax", commands.All(c => c.Syntax.Length > 0),
        string.Join(",", commands.Where(c => c.Syntax.Length == 0).Select(c => c.GlobalName)));

    foreach (var group in commands.GroupBy(c => c.Group).OrderBy(g => g.Key))
        Console.WriteLine($"       {group.Key,-9} {string.Join(" ", group.Select(c => c.GlobalName).OrderBy(n => n))}");

    // Menu coverage: every menu command must resolve in the table, with valid keywords.
    int menuChecked = 0, menuBad = 0;
    void WalkMenu(IReadOnlyList<CommandMenuEntry> items)
    {
        foreach (CommandMenuEntry entry in items)
        {
            if (entry.IsSubmenu) { WalkMenu(entry.Items); continue; }
            if (entry.IsSeparator || entry.Command.Length == 0) continue;

            menuChecked++;
            string name = CommandText.FirstWord(entry.Command);
            if (!runtime.IsKnownCommand(name))
            {
                Console.WriteLine($"       menu '{entry.Header}' -> unknown command {name}");
                menuBad++;
            }
        }
    }
    WalkMenu(CommandMenu.Build());
    Check($"menu commands resolve ({menuChecked} entries)", menuBad == 0);
}
catch (Exception ex)
{
    Check("ViewerCommands registers", false, ex.Message);
}

// ---------- 1b. Coverage of the viewer API ----------
{
    var runtime = new CommandRuntime(new object(), new ViewerCommands());
    var report = CommandCoverage.Analyse(runtime.Commands, typeof(CloudScope.ViewerController));
    Console.WriteLine();
    Console.WriteLine(report.Describe());
    Console.WriteLine();
}

// ---------- 2. Runtime mechanics, against a command class built for it ----------
var log = new List<string>();
var probeRuntime = new CommandRuntime(log, new ProbeCommands());

CommandResult Run(string input) => probeRuntime.Execute(input);

// one-shot
log.Clear();
var r = Run("PING");
Check("one-shot command ends", r.Status == CommandStatus.Ended && r.Message == "pong", r.Message);

// prompt then answer
log.Clear();
r = Run("GREET");
Check("prompts when no argument", r.Status == CommandStatus.Prompting && r.Prompt.StartsWith("Enter name"), r.Prompt);
r = Run("Anna");
Check("answer completes command", r.Status == CommandStatus.Ended && r.Message == "Hello Anna", r.Message);

// argument on the command line skips the prompt
r = Run("GREET Bela");
Check("inline argument skips prompt", r.Status == CommandStatus.Ended && r.Message == "Hello Bela", r.Message);

// multiple tokens satisfy successive prompts (script-style)
r = Run("ADD 2 3");
Check("two tokens answer two prompts", r.Status == CommandStatus.Ended && r.Message == "5", r.Message);

// keyword resolution and abbreviation
r = Run("SHAPE B");
Check("keyword abbreviation resolves", r.Message == "BOX", r.Message);
r = Run("SHAPE Sphere");
Check("full keyword resolves", r.Message == "SPHERE", r.Message);
r = Run("SHAPE");
Check("keyword prompt appears", r.Status == CommandStatus.Prompting);
r = Run("");
Check("Enter takes the default keyword", r.Message == "BOX", r.Message);

// invalid keyword re-asks instead of failing
r = Run("SHAPE zzz");
Check("invalid keyword re-asks", r.Status == CommandStatus.Prompting && r.Message.Contains("Invalid option"), r.Message);
probeRuntime.CancelActive();

// cancellation runs the command's finally block
log.Clear();
r = Run("HOLD");
Check("HOLD suspends", r.Status == CommandStatus.Prompting);
r = Run("ESC");
Check("Escape cancels", r.Status == CommandStatus.Cancelled);
Check("cancel unwinds the command body", log.Contains("cleanup"), string.Join(",", log));

// a keyword at an active prompt beats a command name (the "Z at a filter prompt" case)
log.Clear();
Run("SHAPE");
r = Run("PING");
Check("command name at keyword prompt switches command", r.Message == "pong", r.Message);

// free-form prompts keep text as data, not as a command
Run("GREET");
r = Run("PING");
Check("value prompt keeps text as data", r.Message == "Hello PING", r.Message);

// numbers and ranges
r = Run("LIMIT 500");
Check("range rejects out-of-range", r.Status == CommandStatus.Prompting && r.Message.Contains("between"), r.Message);
r = Run("42");
Check("range accepts valid value", r.Message == "42", r.Message);

// point prompts, answered by a pick
r = Run("PICK");
Check("point prompt waits", r.Status == CommandStatus.Prompting && probeRuntime.AwaitsPoint);
r = probeRuntime.SupplyPoint(new Vector3(1, 2, 3), 10, 20);
Check("viewport pick answers point prompt", r.Message == "1,2,3", r.Message);
r = Run("PICK 4,5,6");
Check("typed point answers point prompt", r.Message == "4,5,6", r.Message);

Check("relative Cartesian point uses its base point",
    PromptPointStep.TryParsePoint("@2,-3,4", new Vector3(10, 20, 30), 30, out Vector3 relativePoint)
    && relativePoint == new Vector3(12, 17, 34), relativePoint.ToString());
Check("relative two-coordinate point preserves base elevation",
    PromptPointStep.TryParsePoint("@2,-3", new Vector3(10, 20, 30), 30, out Vector3 relative2d)
    && relative2d == new Vector3(12, 17, 30), relative2d.ToString());
Check("absolute two-coordinate point uses prompt plane elevation",
    PromptPointStep.TryParsePoint("2,3", new Vector3(10, 20, 30), 7, out Vector3 planarPoint)
    && planarPoint == new Vector3(2, 3, 7), planarPoint.ToString());
Check("relative polar point resolves distance and angle",
    PromptPointStep.TryParsePoint("@10<90", Vector3.Zero, 0, out Vector3 polarPoint)
    && MathF.Abs(polarPoint.X) < 1e-4f && MathF.Abs(polarPoint.Y - 10f) < 1e-4f,
    polarPoint.ToString());

// distances may be typed or measured by the same viewport-pick path
r = Run("DIST");
Check("distance-or-point prompt waits", r.Status == CommandStatus.Prompting && probeRuntime.AwaitsPoint);
r = probeRuntime.SupplyPoint(new Vector3(3, 4, 0), 30, 40);
Check("viewport pick measures distance", r.Message == "5", r.Message);
r = Run("DIST 7.5");
Check("visual distance still accepts typing", r.Message == "7.5", r.Message);

r = Run("DIRECTIONPOINT");
Check("directional point prompt waits", r.Status == CommandStatus.Prompting && probeRuntime.AwaitsPoint);
r = Run("12.5");
Check("typed distance resolves along inferred direction",
    r.Status == CommandStatus.Ended && r.Message == "12.5,0,0", r.Message);

// transparent command inside a suspended one
log.Clear();
Run("GREET");
r = Run("PEEK");
Check("bare name at a value prompt stays data", r.Message == "Hello PEEK", r.Message);
Run("GREET");
r = Run("'PEEK");
Check("apostrophe runs a transparent command mid-command", r.Message == "peeked", r.Message);
r = Run("'GREET");
Check("apostrophe refuses a non-transparent command", r.Message.Contains("cannot be used transparently"), r.Message);
r = Run("Cili");
Check("interrupted command resumes", r.Message == "Hello Cili", r.Message);

// repeat with Enter
Run("PING");
r = Run("");
Check("Enter repeats the last command", r.Message == "pong", r.Message);

// deferred lines (what SCRIPT is built on)
r = Run("BATCH");
Check("deferred lines run after the command", r.Message.Contains("pong"), r.Message);

// unknown command
r = Run("NOSUCHCOMMAND");
Check("unknown command reports", r.Message.Contains("Unknown command"), r.Message);

// ---------- 2b. Prompt layout the shells draw ----------
{
    var options = new PromptOptions("Select shape [Box/CYlinder/Sphere] <Box>:",
        new Keyword("BOX", "Box"), new Keyword("CYLINDER", "CYlinder"), new Keyword("SPHERE", "Sphere"))
    { DefaultKeyword = "BOX" };

    IReadOnlyList<PromptSegment> laid = PromptLayout.Split(options.Message, options);
    Check("prompt layout keeps its text", string.Concat(laid.Select(x => x.Text)) == options.Message,
        string.Concat(laid.Select(x => x.Text)));
    Check("prompt layout finds every keyword",
        laid.Count(x => x.Kind == PromptSegmentKind.Keyword) == 3);
    Check("prompt layout marks the default",
        laid.Single(x => x.IsDefault).Keyword?.GlobalName == "BOX");
    Check("prompt layout leaves a keywordless prompt alone",
        PromptLayout.Split("Specify point:", null) is [{ Kind: PromptSegmentKind.Text, Text: "Specify point:" }]);
    Check("prompt layout appends keywords a message did not list",
        PromptLayout.Split("Answer:", options).Count(x => x.Kind == PromptSegmentKind.Keyword) == 3);

    // Notation that is not a keyword must stay text, or clicking "<0-9>" would submit it.
    var ranged = new PromptOptions("Set level [All/1-9]:", new Keyword("ALL", "All"));
    Check("prompt layout leaves unknown bracket words as text",
        PromptLayout.Split(ranged.Message, ranged).Count(x => x.Kind == PromptSegmentKind.Keyword) == 1);
}

// ---------- 2c. Autocomplete ----------
{
    var vars = new SystemVariableTable();
    double pdsize = 2;
    vars.RegisterReal("PTMAX", "per-frame point budget", () => pdsize, v => pdsize = v, 0.5, 20);
    string[] names = ["ZOOM", "SETVAR", "PAN", "POINTSIZE"];

    var hits = CommandCompletionSource.Complete("z", null, names, null, variables: vars.All);
    Check("completion prefers a prefix match", hits[0].Insert == "ZOOM", hits[0].Insert);

    hits = CommandCompletionSource.Complete("ptmax", null, names, null, variables: vars.All);
    Check("completion offers a variable as SETVAR",
        hits.Any(h => h.Kind == CompletionKind.Variable && h.Insert == "SETVAR PTMAX"),
        string.Join(",", hits.Select(h => h.Insert)));

    hits = CommandCompletionSource.Complete("SETVAR pt", null, names, null, variables: vars.All);
    Check("completion completes a variable name as an argument",
        hits.Count == 1 && hits[0].Insert == "SETVAR PTMAX",
        string.Join(",", hits.Select(h => h.Insert)));

    // Two commands match "p" equally well; the one just run comes first.
    hits = CommandCompletionSource.Complete("p", null, names, null, recentCommands: ["POINTSIZE"]);
    Check("completion puts a recently run command first", hits[0].Insert == "POINTSIZE", hits[0].Insert);
    hits = CommandCompletionSource.Complete("p", null, names, null, recentCommands: ["PAN"]);
    Check("completion recency does not outrank a better match", hits[0].Insert == "PAN", hits[0].Insert);
}

// ---------- 3. System variables ----------
var table = new SystemVariableTable();
double size = 2;
table.RegisterReal("PDSIZE", "point size", () => size, v => size = v, 0.5, 20);
table.RegisterInt("READONLY", "read only", () => 7);
Check("variable writes through", table.Write("PDSIZE", "5").Contains("5") && Math.Abs(size - 5) < 1e-9);
Check("variable rejects out-of-range", table.Write("PDSIZE", "999").Contains("between"));
Check("read-only variable refuses", table.Write("READONLY", "1").Contains("read-only"));
Check("unknown variable reports", table.Read("NOPE").Contains("Unknown"));

// ---------- 4. Undo ----------
var history = new UndoManager();
int value = 0;
history.BeginMark("A"); history.Record(new DelegateUndoAction("inc", () => value--, () => value++)); value++; history.Commit();
history.BeginMark("B"); history.Record(new DelegateUndoAction("inc", () => value--, () => value++)); value++; history.Commit();
Check("undo depth counts commands", history.Depth == 2, history.Depth.ToString());
history.Undo();
Check("undo reverses one command", value == 1, value.ToString());
history.Redo();
Check("redo reapplies", value == 2, value.ToString());
history.Undo(2);
Check("undo takes a count", value == 0, value.ToString());
history.BeginMark("C"); history.Record(new DelegateUndoAction("inc", () => value--, () => value++)); value++;
history.Rollback();
Check("rollback reverses a cancelled command", value == 0, value.ToString());

// ---------- 5. Cross-section geometry and orthographic camera ----------
var section = new SectionDefinition(1, "XS1", new Vector3(0f, 0f, 2f), new Vector3(10f, 0f, 4f), 2f);
SectionClip clip = section.ToClip();
Check("section keeps a point in the slab", clip.Contains(new Vector3(5f, 0.9f, 100f)));
Check("section rejects width overflow", !clip.Contains(new Vector3(5f, 1.1f, 3f)));
Check("section rejects length overflow", !clip.Contains(new Vector3(10.1f, 0f, 3f)));
Check("section intersects a crossing cell", clip.IntersectsAabb(new Vector3(4f, 0.5f, -10f), new Vector3(6f, 2f, 10f)));
Check("section rejects a remote cell", !clip.IntersectsAabb(new Vector3(20f, 20f, -10f), new Vector3(21f, 21f, 10f)));

var sectionCamera = new CloudScope.OrbitCamera();
sectionCamera.SetOrthographicSectionView(section.Start, section.End, flipped: false, cloudRadius: 50f);
Check("section camera is orthographic", !sectionCamera.IsPerspective);
Check("section camera keeps Z vertical", Vector3.Dot(sectionCamera.CameraUp, Vector3.UnitZ) > 0.999f);
Check("section camera follows baseline", Vector3.Dot(sectionCamera.CameraRight, Vector3.UnitX) > 0.999f);

var sectionGrips = new CrossSectionGripTarget(section);
Check("section exposes six standard grips", sectionGrips.Grips.Count == 6);
Check("section endpoint grips follow baseline",
    sectionGrips.GetGrip(CrossSectionGripTarget.StartGrip).Position == section.Start
    && sectionGrips.GetGrip(CrossSectionGripTarget.EndGrip).Position == section.End);
Check("section width grips are symmetric",
    Vector3.Distance(
        sectionGrips.GetGrip(CrossSectionGripTarget.PositiveWidthGrip).Position,
        sectionGrips.GetGrip(CrossSectionGripTarget.NegativeWidthGrip).Position) is > 1.999f and < 2.001f);
Check("section grip kinds describe their behavior",
    sectionGrips.GetGrip(CrossSectionGripTarget.CenterGrip).Kind == GripKind.Center
    && sectionGrips.GetGrip(CrossSectionGripTarget.DirectionGrip).Kind == GripKind.Direction);
Check("section contributes endpoint and midpoint snaps",
    sectionGrips.SnapPoints.Count == 3
    && sectionGrips.SnapPoints.Count(point => point.Kind == ObjectSnapKind.Endpoint) == 2
    && sectionGrips.SnapPoints.Any(point => point.Kind == ObjectSnapKind.Midpoint));

var polyline = new Polyline3D(1, "PL1",
    [new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 5f)]);
var polylineGrips = new PolylineGripTarget(polyline);
Check("open polyline exposes vertex and midpoint grips", polylineGrips.Grips.Count == 5);
Check("polyline contributes endpoint and midpoint snaps",
    polylineGrips.SnapPoints.Count == 5
    && polylineGrips.SnapPoints.Count(point => point.Kind == ObjectSnapKind.Midpoint) == 2);
var selfSnapSource = new FilteredObjectSnapSource(polylineGrips, excludedSourceIndex: 0);
Check("active endpoint exclusion keeps the other grips snappable",
    selfSnapSource.SnapPoints.All(point => point.SourceIndex != 0)
    && selfSnapSource.SnapPoints.Any(point => point.SourceIndex == 1));

var dragCamera = new CloudScope.OrbitCamera();
dragCamera.SetViewportSize(800, 600);
dragCamera.SetOrthographicSectionView(Vector3.Zero, Vector3.UnitX, flipped: false, cloudRadius: 50f);
var (vertexX, vertexY, _) = dragCamera.WorldToScreen(polyline.Vertices[0]);
polylineGrips.BeginHandleDrag(0, (int)vertexX, (int)vertexY, dragCamera);
polylineGrips.UpdateHandleDragTo(new Vector3(2f, 3f, 4f));
polylineGrips.EndHandleDrag();
Check("polyline vertex grip accepts an exact snapped point",
    polylineGrips.Polyline.Vertices[0] == new Vector3(2f, 3f, 4f));

var snapEngine = new ObjectSnapEngine();
var endpoint = polylineGrips.Polyline.Vertices[0];
var (endpointX, endpointY, endpointBehind) = dragCamera.WorldToScreen(endpoint);
ObjectSnapResult endpointSnap = snapEngine.Resolve(Vector3.Zero,
    (int)endpointX, (int)endpointY, dragCamera, [polylineGrips], null);
Check("object snap resolves a polyline grip", !endpointBehind
    && endpointSnap.Kind == ObjectSnapKind.Endpoint
    && endpointSnap.Position == endpoint);
var otherEndpoint = polylineGrips.Polyline.Vertices[1];
var (otherX, otherY, otherBehind) = dragCamera.WorldToScreen(otherEndpoint);
ObjectSnapResult selfEndpointSnap = snapEngine.Resolve(Vector3.Zero,
    (int)otherX, (int)otherY, dragCamera,
    [new FilteredObjectSnapSource(polylineGrips, excludedSourceIndex: 0)], endpoint);
Check("dragged endpoint snaps to another endpoint on its own polyline", !otherBehind
    && selfEndpointSnap.Kind == ObjectSnapKind.Endpoint
    && selfEndpointSnap.Position == otherEndpoint);

Vector3 axisTarget = new(4f, 0f, 0f);
var (axisX, axisY, _) = dragCamera.WorldToScreen(axisTarget);
ObjectSnapResult axisSnap = snapEngine.Resolve(axisTarget,
    (int)axisX, (int)axisY, dragCamera, Array.Empty<IObjectSnapSource>(), Vector3.Zero,
    orthoMode: true);
Check("ortho locks point input onto the world X direction",
    axisSnap.Kind == ObjectSnapKind.AxisX && MathF.Abs(axisSnap.Position.X - 4f) < 0.1f);
ObjectSnapResult freeSnap = snapEngine.Resolve(axisTarget,
    (int)axisX + 40, (int)axisY + 40, dragCamera, Array.Empty<IObjectSnapSource>(), Vector3.Zero);
Check("without ortho the cursor is never pulled onto an axis",
    freeSnap.Kind == ObjectSnapKind.None && freeSnap.Position == axisTarget);
var (offAxisX, offAxisY, _) = dragCamera.WorldToScreen(new Vector3(4f, 0f, 1.5f));
ObjectSnapResult orthoFarSnap = snapEngine.Resolve(Vector3.Zero,
    (int)offAxisX, (int)offAxisY, dragCamera, Array.Empty<IObjectSnapSource>(), Vector3.Zero,
    orthoMode: true);
Check("ortho locks onto the nearest axis even far from it",
    orthoFarSnap.IsAxis && MathF.Abs(orthoFarSnap.Position.X - 4f) < 0.2f);

var orbitCamera = new CloudScope.OrbitCamera();
orbitCamera.SetViewportSize(800, 600);
orbitCamera.SetOrthographicStandardView("ISOMETRIC", 50f);

Vector3 verticalTarget = new(0f, 0f, 6f);
var (verticalX, verticalY, verticalBehind) = orbitCamera.WorldToScreen(verticalTarget);
ObjectSnapResult verticalSnap = snapEngine.Resolve(Vector3.Zero,
    (int)verticalX, (int)verticalY, orbitCamera, Array.Empty<IObjectSnapSource>(), Vector3.Zero,
    orthoMode: true);
Check("ortho locks the world Z direction in a rotated view", !verticalBehind
    && verticalSnap.Kind == ObjectSnapKind.AxisZ
    && MathF.Abs(verticalSnap.Position.Z - 6f) < 0.25f
    && verticalSnap.Position.Xy.Length < 0.05f);

Vector3[] verticalArm = CursorCrosshairGeometry.Arm(verticalTarget, 2, orbitCamera);
Vector3[] pickBox = CursorCrosshairGeometry.PickBox(verticalTarget, orbitCamera);
float armLength = (verticalArm[1] - verticalArm[0]).Length
    / orbitCamera.WorldUnitsPerPixel(verticalTarget);
Check("cursor crosshair sizes the Z arm in pixels and keeps it vertical",
    verticalArm.Length == 2
    && (verticalArm[1] - verticalArm[0]).Xy.Length < 1e-4f
    && MathF.Abs(armLength - 2f * CursorCrosshairGeometry.ArmPixels) < 0.5f);
var (boxLeftX, boxLeftY, _) = orbitCamera.WorldToScreen(pickBox[0]);
var (boxRightX, boxRightY, _) = orbitCamera.WorldToScreen(pickBox[1]);
var (boxTopX, boxTopY, _) = orbitCamera.WorldToScreen(pickBox[2]);
float boxWidth = MathF.Sqrt((boxRightX - boxLeftX) * (boxRightX - boxLeftX)
    + (boxRightY - boxLeftY) * (boxRightY - boxLeftY));
float boxHeight = MathF.Sqrt((boxTopX - boxRightX) * (boxTopX - boxRightX)
    + (boxTopY - boxRightY) * (boxTopY - boxRightY));
Check("cursor pick box stays a screen-aligned square in a rotated view",
    pickBox.Length == 4
    && MathF.Abs(boxWidth - boxHeight) < 0.01f
    && MathF.Abs(boxWidth - 2f * CursorCrosshairGeometry.PickBoxPixels) < 0.5f
    && MathF.Abs(boxRightY - boxLeftY) < 0.01f
    && MathF.Abs(boxTopX - boxRightX) < 0.01f);
Check("crosshair and axis snapping read their colour from the shared axis palette",
    Enumerable.Range(0, 3).All(axis =>
        CursorCrosshairGeometry.ArmColor(axis, CursorCrosshairGeometry.KindOf(axis)).Xyz
            == AxisPalette.Of(axis)
        && GripOverlayGeometry.SnapColor(CursorCrosshairGeometry.KindOf(axis)).Xyz
            == AxisPalette.Of(axis)));
Check("the locked axis is the only lit crosshair arm",
    CursorCrosshairGeometry.ArmColor(2, ObjectSnapKind.AxisZ).W
    > CursorCrosshairGeometry.ArmColor(0, ObjectSnapKind.AxisZ).W);

int openSegments = PolylineRenderGeometry.SegmentCount(polyline.Vertices.Length, closed: false);
int openJoins = PolylineRenderGeometry.JoinCount(polyline.Vertices.Length, closed: false);
float[] segmentInstances = PolylineRenderGeometry.BuildSegmentInstances(polyline.Vertices, closed: false);
float[] joinInstances = PolylineRenderGeometry.BuildJoinInstances(polyline.Vertices, closed: false);
Check("polyline expands into one segment instance per edge and one join per bend",
    openSegments == 2 && openJoins == 1
    && segmentInstances.Length == openSegments * PolylineRenderGeometry.SegmentFloats
    && joinInstances.Length == openJoins * PolylineRenderGeometry.JoinFloats);
Check("open polyline marks exact start and end caps",
    segmentInstances.AsSpan(0, 3).SequenceEqual(segmentInstances.AsSpan(3, 3))
    && segmentInstances.AsSpan(segmentInstances.Length - 6, 3)
        .SequenceEqual(segmentInstances.AsSpan(segmentInstances.Length - 3, 3)));
float[] closedInstances = PolylineRenderGeometry.BuildSegmentInstances(polyline.Vertices, closed: true);
Check("closed polyline adds its closing render segment",
    PolylineRenderGeometry.SegmentCount(polyline.Vertices.Length, closed: true) == 3
    && PolylineRenderGeometry.JoinCount(polyline.Vertices.Length, closed: true) == 3
    && closedInstances.AsSpan(closedInstances.Length - 6, 3)
        .SequenceEqual(closedInstances.AsSpan(3, 3)));
float[] packedPolyline = polyline.Vertices.SelectMany(point => new[] { point.X, point.Y, point.Z }).ToArray();
float[] commonClosedGeometry = PolylineRenderGeometry.BuildSegmentInstances(
    packedPolyline, polyline.Vertices.Length, closed: true);
Check("pivot and object paths share the closed line builder",
    commonClosedGeometry.AsSpan().SequenceEqual(closedInstances));

var planarArc = new PlanarPolyline(2, "PL2", Vector3.Zero, Vector3.UnitX, Vector3.UnitY,
[
    new PlanarPolylineVertex(Vector2.Zero,
        PlanarPolylineGeometry.TangentArcBulge(Vector2.Zero, new Vector2(10, 10), Vector2.UnitX)),
    new PlanarPolylineVertex(new Vector2(10, 10))
]);
Vector3[] arcPoints = PlanarPolylineGeometry.Tessellate(planarArc);
Check("planar polyline arc tessellates from exact start to exact end",
    arcPoints.Length > 2 && arcPoints[0] == Vector3.Zero
    && Vector3.DistanceSquared(arcPoints[^1], new Vector3(10, 10, 0)) < 1e-6f);
Check("tangent arc initially follows the requested direction",
    arcPoints.Length > 2 && arcPoints[1].X > 0f && MathF.Abs(arcPoints[1].Y) < arcPoints[1].X);
Check("PLINE angle option encodes a quarter circle",
    MathF.Abs(PlanarPolylineGeometry.IncludedAngleBulge(90f) - 0.41421356f) < 1e-5f);
Check("PLINE three-point option preserves the requested side",
    PlanarPolylineGeometry.ThreePointArcBulge(
        Vector2.Zero, new Vector2(5, -2), new Vector2(10, 0)) > 0f);
Check("PLINE radius rejects an impossible chord",
    PlanarPolylineGeometry.RadiusArcBulge(
        Vector2.Zero, new Vector2(10, 0), 4f, Vector2.UnitX) == 0f);
var tapered = planarArc with
{
    Vertices =
    [
        planarArc.Vertices[0] with { StartWidth = 2f, EndWidth = 4f },
        planarArc.Vertices[1]
    ]
};
(Vector3[] leftEdge, Vector3[] rightEdge) = PlanarPolylineGeometry.TessellateWidthEdges(tapered);
Check("wide arc produces matching tapered edge paths",
    leftEdge.Length == arcPoints.Length && rightEdge.Length == arcPoints.Length
    && Vector3.Distance(leftEdge[0], rightEdge[0]) is > 1.999f and < 2.001f
    && Vector3.Distance(leftEdge[^1], rightEdge[^1]) is > 3.999f and < 4.001f);
var planarGrips = new PlanarPolylineGripTarget(tapered);
planarGrips.SetUniformWidth(3f);
Check("PEDIT width updates every planar segment",
    planarGrips.Polyline.Vertices[0].StartWidth == 3f
    && planarGrips.Polyline.Vertices[0].EndWidth == 3f);
Vector3 oldStart = planarGrips.Polyline.ToWorld(planarGrips.Polyline.Vertices[0].Position);
Vector3 oldEnd = planarGrips.Polyline.ToWorld(planarGrips.Polyline.Vertices[^1].Position);
planarGrips.Reverse();
Check("PEDIT reverse swaps endpoints",
    planarGrips.Polyline.Endpoint == oldStart
    && planarGrips.Polyline.ToWorld(planarGrips.Polyline.Vertices[0].Position) == oldEnd);
var gripCamera = new CloudScope.OrbitCamera();
gripCamera.SetViewportSize(800, 600);
GripDescriptor[] endpointMarker =
[
    new GripDescriptor(0, GripKind.Endpoint, Vector3.Zero, Vector3.Zero, GripConstraint.ViewPlane)
];
float[] normalGrip = new float[GripOverlayGeometry.FloatsPerGrip];
float[] retinaGrip = new float[GripOverlayGeometry.FloatsPerGrip];
GripOverlayGeometry.Fill(endpointMarker, gripCamera, normalGrip, displayScale: 1f);
GripOverlayGeometry.Fill(endpointMarker, gripCamera, retinaGrip, displayScale: 2f);
Check("Retina CAD grip keeps its logical size",
    MathF.Abs(retinaGrip[0]) > MathF.Abs(normalGrip[0]) * 1.99f);
float[] midpointMarker = new float[GripOverlayGeometry.FloatsPerGrip];
GripOverlayGeometry.Fill(
    [new GripDescriptor(0, GripKind.Midpoint, Vector3.Zero, Vector3.Zero, GripConstraint.ViewPlane)],
    gripCamera, midpointMarker);
Check("endpoint and midpoint use distinct CAD marker shapes",
    !normalGrip.AsSpan().SequenceEqual(midpointMarker));
string planarJson = PlanarPolylineFile.Serialize([tapered]);
PlanarPolyline planarRoundTrip = PlanarPolylineFile.Deserialize(planarJson).Single();
Check("planar polyline JSON preserves plane, arc and tapered widths",
    planarRoundTrip.Origin == tapered.Origin
    && planarRoundTrip.AxisX == tapered.AxisX
    && planarRoundTrip.AxisY == tapered.AxisY
    && planarRoundTrip.Vertices.AsSpan().SequenceEqual(tapered.Vertices));

// ---------- 6. Seeded individual-tree segmentation ----------
var forest = new List<PointData>();
for (int z = 0; z <= 30; z++)
{
    forest.Add(new PointData { X = 0f, Y = 0f, Z = z * 0.1f });
    forest.Add(new PointData { X = 2f, Y = 0f, Z = z * 0.1f });
}
for (int x = 0; x <= 10; x++)
{
    forest.Add(new PointData { X = x * 0.2f, Y = 0f, Z = 3f });
    forest.Add(new PointData { X = 2f - x * 0.2f, Y = 0.1f, Z = 3.1f });
}
TreeSegmentationResult tree = TreeSegmentation.Segment(forest, new Vector3(0f, 0f, 1.3f),
    new TreeSegmentationOptions { ConnectionRadius = 0.23f, MinimumTrunkPoints = 8 });
Check("tree segmentation grows from a trunk seed", tree.Succeeded && tree.PointIndices.Count > 20,
    tree.FailureReason ?? tree.PointIndices.Count.ToString());
Check("tree segmentation detects the neighbouring trunk as a competitor", tree.CompetitorCount > 0,
    tree.CompetitorCount.ToString());
Check("tree segmentation does not absorb the competing trunk",
    tree.PointIndices.All(i => forest[i].X < 1.5f),
    tree.PointIndices.Count.ToString());

var terrainCloud = new List<PointData>();
var expectedGround = new HashSet<int>();
for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
{
    expectedGround.Add(terrainCloud.Count);
    terrainCloud.Add(new PointData { X = x * 0.25f, Y = y * 0.25f, Z = x * 0.025f });
    if ((x + y) % 3 == 0)
        terrainCloud.Add(new PointData { X = x * 0.25f, Y = y * 0.25f, Z = 1.2f + x * 0.025f });
}
GroundSegmentationResult terrain = GroundSegmentation.Segment(terrainCloud);
Check("ground segmentation follows a sloping terrain surface", terrain.Succeeded
    && expectedGround.All(terrain.PointIndices.Contains), terrain.PointIndices.Count.ToString());
Check("ground segmentation excludes vegetation above the surface",
    terrain.PointIndices.All(expectedGround.Contains), terrain.PointIndices.Count.ToString());

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

sealed class ProbeCommands
{
    [CommandMethod("PING", Summary = "Replies pong.", Syntax = "PING")]
    public IEnumerable<PromptStep> Ping(CommandContext c) { c.Editor.WriteMessage("pong"); yield break; }

    [CommandMethod("DIRECTIONPOINT", Summary = "Directional point probe.", Syntax = "DIRECTIONPOINT")]
    public IEnumerable<PromptStep> DirectionPoint(CommandContext c)
    {
        PromptPointStep point = c.Editor.GetPoint("Specify distance:")
            .WithDirectionalDistance(distance => new Vector3((float)distance, 0f, 0f));
        yield return point;
        if (point.IsOk)
            c.Editor.WriteMessage(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{point.Value.X:0.###},{point.Value.Y:0.###},{point.Value.Z:0.###}"));
    }

    [CommandMethod("PEEK", Flags = CommandFlags.Transparent | CommandFlags.NoHistory,
        Summary = "Transparent probe.", Syntax = "PEEK")]
    public IEnumerable<PromptStep> Peek(CommandContext c) { c.Editor.WriteMessage("peeked"); yield break; }

    [CommandMethod("GREET", Summary = "Greets by name.", Syntax = "GREET <name>")]
    public IEnumerable<PromptStep> Greet(CommandContext c)
    {
        var name = c.Editor.GetString("Enter name:");
        yield return name;
        if (!name.IsOk) yield break;
        c.Editor.WriteMessage($"Hello {name.Value}");
    }

    [CommandMethod("ADD", Summary = "Adds two numbers.", Syntax = "ADD <a> <b>")]
    public IEnumerable<PromptStep> Add(CommandContext c)
    {
        var a = c.Editor.GetInteger("Enter first:");
        yield return a;
        if (!a.IsOk) yield break;
        var b = c.Editor.GetInteger("Enter second:");
        yield return b;
        if (!b.IsOk) yield break;
        c.Editor.WriteMessage((a.Value + b.Value).ToString());
    }

    [CommandMethod("SHAPE", Summary = "Picks a shape.", Syntax = "SHAPE [Box/Sphere]")]
    public IEnumerable<PromptStep> Shape(CommandContext c)
    {
        var s = c.Editor
            .GetKeywords("Specify shape [Box/Sphere] <Box>:", new Keyword("BOX", "Box"), new Keyword("SPHERE", "Sphere"))
            .WithDefaultKeyword("BOX");
        yield return s;
        c.Editor.WriteMessage(s.Keyword);
    }

    [CommandMethod("LIMIT", Summary = "Takes a bounded number.", Syntax = "LIMIT <0-100>")]
    public IEnumerable<PromptStep> Limit(CommandContext c)
    {
        var v = c.Editor.GetInteger("Enter value 0-100:").WithRange(0, 100);
        yield return v;
        if (!v.IsOk) yield break;
        c.Editor.WriteMessage(v.Value.ToString());
    }

    [CommandMethod("PICK", Summary = "Takes a point.", Syntax = "PICK <x,y,z>")]
    public IEnumerable<PromptStep> Pick(CommandContext c)
    {
        var p = c.Editor.GetPoint("Specify point:");
        yield return p;
        if (!p.IsOk) yield break;
        c.Editor.WriteMessage($"{p.Value.X:0.#},{p.Value.Y:0.#},{p.Value.Z:0.#}");
    }

    [CommandMethod("DIST", Summary = "Measures a typed or picked distance.", Syntax = "DIST <distance>")]
    public IEnumerable<PromptStep> Distance(CommandContext c)
    {
        var distance = c.Editor.GetDistanceOrPoint("Specify distance:", point => point.Length);
        yield return distance;
        if (!distance.IsOk) yield break;
        c.Editor.WriteMessage(distance.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    [CommandMethod("HOLD", Summary = "Suspends to test cancellation.", Syntax = "HOLD")]
    public IEnumerable<PromptStep> Hold(CommandContext c)
    {
        var log = (List<string>)c.Target;
        bool finished = false;
        try
        {
            var s = c.Editor.GetString("Waiting:");
            yield return s;
            finished = true;
            c.Editor.WriteMessage("done");
        }
        finally
        {
            if (!finished) log.Add("cleanup");
        }
    }

    [CommandMethod("BATCH", Summary = "Queues deferred lines.", Syntax = "BATCH")]
    public IEnumerable<PromptStep> Batch(CommandContext c)
    {
        c.Editor.WriteMessage("queued");
        c.Editor.RunAfter("PING");
        yield break;
    }
}
