using System.Reflection;
using CloudScope;
using CloudScope.Commands;
using CloudScope.Forestry;
using CloudScope.Loading;
using CloudScope.Rendering;
using CloudScope.Spatial;
using CloudScope.Ui;
using CloudScope.Ui.Commands;
using OpenTK.Mathematics;

internal static class ReviewChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        void Verify(string name, bool ok) => check(name, ok, "");
        var runtime = new CommandRuntime(new object(), new ReviewProbe());
        foreach (string input in new[] { "NaN", "Infinity", "-Infinity", "1e999" })
        {
            runtime.CancelActive();
            Verify($"Real prompts reject {input}", runtime.Execute("REAL " + input).Status == CommandStatus.Prompting);
        }
        foreach (string input in new[] { "NaN,1,2", "1,,2", "1,2,", "1e99,1,2", "@1e99<0", "@3e38,0,0" })
            Verify($"Point parser rejects invalid/overflowing {input}",
                !PromptPointStep.TryParsePoint(input, new Vector3(3e38f, 0, 0), 0, out _));
        runtime.CancelActive();
        runtime.Execute("POINT");
        Verify("Viewport rejects non-finite picks", runtime.SupplyPoint(new Vector3(float.NaN, 0, 0), 0, 0).Status == CommandStatus.Prompting);
        runtime.CancelActive();
        var variables = new SystemVariableTable();
        double value = 3;
        variables.RegisterReal("SIZE", "Probe", () => value, v => value = v, 0, 10);
        variables.Write("SIZE", "NaN");
        Verify("System variables reject NaN without mutation", value == 3);

        runtime.CancelActive();
        Verify("Quoted file argument keeps following limit", runtime.Execute("FILE \"a b.xyz\" 7").Message == "a b.xyz:7");
        Verify("Quoted line argument preserves name and instance", runtime.Execute("LINE \"Tree 1\" 5").Message == "\"Tree 1\" 5");
        Verify("Nested deferred commands run before caller continues", runtime.Execute("OUTER").Message == "inner\nouter");
        Verify("Blank script answer takes a default", runtime.Execute("DEFAULTBATCH").Message == "9");
        var failed = runtime.Execute("BADBATCH");
        Verify("Unknown script command stops following mutations", failed.Status == CommandStatus.Failed && !failed.Message.Contains("outer"));
        Verify("Unknown top-level commands fail", runtime.Execute("NO_SUCH_COMMAND").Status == CommandStatus.Failed);
        var invalidBatch = runtime.Execute("INVALIDBATCH");
        Verify("Invalid script numbers stop instead of consuming subsequent commands", invalidBatch.Status == CommandStatus.Failed && !invalidBatch.Message.Contains("outer"));
        Verify("Recursive scripts terminate with a bounded failure", runtime.Execute("LOOP").Status == CommandStatus.Failed && !runtime.HasActiveCommand);
        Verify("Command matching treats tabs as whitespace", CommandText.FirstWord("POINTSIZE\t3") == "POINTSIZE" && CommandText.TryMatch("POINTSIZE\t3", "POINTSIZE", out string argument) && argument == "3");

        // Exact neighbours are checked against an independent brute-force oracle, including
        // ties, duplicate positions, isolated points and highly nonuniform densities.
        var random = new Random(91);
        var points = Enumerable.Range(0, 257).Select(i => i < 200
            ? new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble())
            : new Vector3(i * 100, i % 3, 0)).Append(Vector3.Zero).Append(Vector3.Zero).ToArray();
        var index = new PointSpatialIndex(points);
        bool exact = true;
        foreach (int k in new[] { 1, 3, 15, 400 })
        for (int i = 0; i < points.Length; i++)
        {
            int origin = i;
            var expected = Enumerable.Range(0, points.Length).Where(j => j != origin)
                .OrderBy(j => Squared(points[j], points[origin])).ThenBy(j => j).Take(k);
            exact &= index.Nearest(i, k).SequenceEqual(expected);
        }
        Verify("Exact spatial index matches brute force for every query and density", exact);
        foreach (float scale in new[] { 1e-6f, 1f, 1e6f })
        {
            var sample = (from y in Enumerable.Range(0, 5) from x in Enumerable.Range(0, 5)
                select new PointData { X = x * scale, Y = y * scale, Z = (x + 2 * y) * scale }).ToArray();
            var mesh = SurfaceReconstruction.Build(Dataset(sample), 8, 4 * scale);
            Vector3 normal = Vector3.Normalize(new Vector3(-1, -2, 1));
            Verify($"PCA normals are invariant at scale {scale}", mesh.Normals.All(n => Math.Abs(Vector3.Dot(n, normal)) > .9999f));
        }

        var annotations = new CloudScope.Labeling.LabelManager();
        var history = new UndoManager();
        annotations.ActionRecorded += history.Record;
        for (int i = 0; i < 250; i++) annotations.ApplyLabel([0], $"label{i}");
        Verify("Unified command undo survives the label manager's local history limit", history.Undo(250) == 250 && annotations.Count == 0);
        Verify("Unified command redo restores all 250 label changes", history.Redo(250) == 250 && annotations.GetLabel(0) == "label249");
        bool conversionRejected = false;
        try { CommandNumber.ToSingle(1e100); } catch (ArgumentOutOfRangeException) { conversionRejected = true; }
        Verify("Numeric geometry conversion rejects finite double overflow", conversionRejected);

        var partial = new PointCloudDataset(new PointData[20], 3, 1, false, 1,
            new PointCloudAttributes(new byte[3], new ushort[3], new byte[3], new double[3], 0, 0));
        partial.ApplyFilter(null, recolor: false);
        Verify("Unused buffer capacity never becomes visible points", partial.VisibleCount == 3);
        var largeFrame = PointCloudFrame.FromBounds(-1e25, 0, 0, 1e25, 0, 0);
        Verify("Cloud radius avoids intermediate float-square overflow", float.IsFinite(largeFrame.Radius) && largeFrame.Radius > 0);
        var distantFrame = PointCloudFrame.FromBounds(1e308, 0, 0, 1e308, 0, 0);
        Verify("Distant world origin avoids double addition overflow", distantFrame.X == 1e308 && distantFrame.Radius == 0);
        bool oversized = false;
        try { PointCloudFrame.FromBounds(-1e100, 0, 0, 1e100, 0, 0); }
        catch (InvalidDataException) { oversized = true; }
        Verify("Unrenderable cloud extent is rejected before upload", oversized);
        var groundPoints = new[] { new PointData(), new PointData { X = 1, Y = 1 } };
        Verify("CSF rejects NaN parameters", !GroundSegmentation.Segment(groundPoints,
            new GroundSegmentationOptions { TimeStep = double.NaN }).Succeeded);
        Verify("CSF rejects non-finite coordinates", !GroundSegmentation.Segment([new PointData { X = float.NaN }]).Succeeded);
        Verify("CSF validates grid extent before integer conversion", !GroundSegmentation.Segment(
            [new PointData { X = -1e30f }, new PointData { X = 1e30f }]).Succeeded);
        Verify("Tree segmentation never grows from an excluded ground seed", !TreeSegmentation.Segment(
            [new PointData()], Vector3.Zero, excludedPointIndices: new HashSet<int> { 0 }).Succeeded);
        Verify("Tree segmentation rejects non-finite parameters", !TreeSegmentation.Segment(
            groundPoints, Vector3.Zero, new TreeSegmentationOptions { ConnectionRadius = float.NaN }).Succeeded);

        using var viewer = new ViewerController(1280, 720, new HeadlessBackend());
        viewer.Load();
        viewer.LoadPointCloud(Dataset(Enumerable.Range(0, 25).Select(i => new PointData { X = i % 5, Y = i / 5, Z = i % 3 }).ToArray()));
        var commands = new ViewerCommandDispatcher(viewer);
        // Every real command and alias must at least start, prompt, cancel and unwind on a
        // live controller. This complements registration checks; it is not GPU coverage.
        foreach (var command in commands.Commands)
        {
            foreach (string name in new[] { command.GlobalName }.Concat(command.Aliases))
            {
                viewer.LoadPointCloud(Dataset(Enumerable.Range(0, 25).Select(i => new PointData { X = i % 5, Y = i / 5, Z = i % 3 }).ToArray()));
                var result = commands.Execute(name);
                Verify($"Real command/alias starts and unwinds: {name}", result.Status != CommandStatus.Failed);
                commands.CancelActive();
            }
        }
        viewer.LoadPointCloud(Dataset(Enumerable.Range(0, 25).Select(i => new PointData { X = i % 5, Y = i / 5, Z = i % 3 }).ToArray()));
        foreach (string line in new[] { "POINTSIZE 3", "COLORBY Class", "FILTER Z 0 2", "FILTER Clear", "THIN 50", "THIN 100", "PROJECTION Parallel", "VIEW Top", "VPORTS Four Top", "ORTHO ON", "LABEL \"Tree 1\" 5", "INSTANCE 7", "SETVAR PDSIZE 4", "EXPLORER Right", "PROPERTIES OFF", "RIBBON OFF" })
        {
            commands.CancelActive();
            var result = commands.Execute(line);
            Verify($"Real command executes: {line}", result.Status == CommandStatus.Ended);
        }
        check("Real LABEL preserves quoted numeric name", viewer.CurrentLabel == "Tree 1" && viewer.CurrentInstanceId == 7, $"label={viewer.CurrentLabel}; instance={viewer.CurrentInstanceId}");
        Verify("Real SETVAR changes shared point size", viewer.PointSize == 4);
        Verify("Real UI commands update canonical viewer state", viewer.PalettesOnRight && !viewer.PropertiesVisible && !viewer.RibbonVisible);
        commands.Execute("SETVAR CINSTANCE invalid");
        Verify("Invalid CINSTANCE preserves the existing instance", viewer.CurrentInstanceId == 7);
        commands.Execute("SETVAR PDSIZE NaN");
        Verify("Real SETVAR keeps valid state on invalid input", viewer.PointSize == 4);
    }

    private static double Squared(Vector3 a, Vector3 b) =>
        ((double)a.X - b.X) * ((double)a.X - b.X) + ((double)a.Y - b.Y) * ((double)a.Y - b.Y) + ((double)a.Z - b.Z) * ((double)a.Z - b.Z);
    private static PointCloudDataset Dataset(PointData[] p) => new(p, p.Length, 20, false, 1,
        new PointCloudAttributes(new byte[p.Length], new ushort[p.Length], new byte[p.Length], p.Select(x => (double)x.Z).ToArray(), 0, 20));
}

public class NullRenderer : DispatchProxy
{
    public static T Create<T>() where T : class => Create<T, NullRenderer>();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.ReturnType == typeof(void)
        ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
}

internal sealed class HeadlessBackend(ISurfaceRenderer? surface = null) : IRenderBackend
{
    public RenderBackendKind Kind => RenderBackendKind.OpenGL;
    public void Initialize() { }
    public void Resize(int width, int height) { }
    public void SetViewport(int x, int y, int width, int height) { }
    public IRenderFrameSession BeginFrame() => throw new NotSupportedException("Headless command checks do not render frames.");
    public IPointCloudRenderer CreatePointCloudRenderer() => NullRenderer.Create<IPointCloudRenderer>();
    public IPointTileCloudRenderer CreateStreamingPointCloudRenderer() => NullRenderer.Create<IPointTileCloudRenderer>();
    public IHighlightRenderer CreateHighlightRenderer() => NullRenderer.Create<IHighlightRenderer>();
    public IOverlayRenderer CreateOverlayRenderer() => NullRenderer.Create<IOverlayRenderer>();
    public ISurfaceRenderer CreateSurfaceRenderer() => surface ?? NullRenderer.Create<ISurfaceRenderer>();
    public IDepthPicker CreateDepthPicker() => NullRenderer.Create<IDepthPicker>();
    public SelectionGizmoRenderers CreateSelectionGizmoRenderers() => new(NullRenderer.Create<IBoxSelectionGizmoRenderer>(), NullRenderer.Create<ISelectionGizmoRenderer>(), NullRenderer.Create<ISelectionGizmoRenderer>());
}

internal sealed class ReviewProbe
{
    [CommandMethod("REAL", Summary = "Real probe", Syntax = "REAL <value>")]
    public IEnumerable<PromptStep> Real(CommandContext c) { var p = c.Editor.GetDouble("Value:"); yield return p; }
    [CommandMethod("POINT", Summary = "Point probe", Syntax = "POINT <point>")]
    public IEnumerable<PromptStep> Point(CommandContext c) { yield return c.Editor.GetPoint("Point:"); }
    [CommandMethod("FILE", Summary = "File probe", Syntax = "FILE <path> <limit>")]
    public IEnumerable<PromptStep> File(CommandContext c)
    {
        var p = c.Editor.GetFileNameForOpen("Path:"); yield return p;
        var n = c.Editor.GetInteger("Limit:"); yield return n;
        c.Editor.WriteMessage($"{p.Value}:{n.Value}");
    }
    [CommandMethod("LINE", Summary = "Line probe", Syntax = "LINE <text>")]
    public IEnumerable<PromptStep> Line(CommandContext c) { var p = c.Editor.GetLine("Text:"); yield return p; c.Editor.WriteMessage(p.Value); }
    [CommandMethod("INNER", Summary = "Nested probe", Syntax = "INNER")]
    public IEnumerable<PromptStep> Inner(CommandContext c) { c.Editor.RunAfter("LINE inner"); yield break; }
    [CommandMethod("OUTER", Summary = "Caller probe", Syntax = "OUTER")]
    public IEnumerable<PromptStep> Outer(CommandContext c) { c.Editor.RunAfter("INNER"); c.Editor.RunAfter("LINE outer"); yield break; }
    [CommandMethod("DEFAULT", Summary = "Default probe", Syntax = "DEFAULT [n]")]
    public IEnumerable<PromptStep> Default(CommandContext c) { var p = c.Editor.GetInteger("N:").WithDefault(9); yield return p; c.Editor.WriteMessage(p.Value.ToString()); }
    [CommandMethod("DEFAULTBATCH", Summary = "Default script probe", Syntax = "DEFAULTBATCH")]
    public IEnumerable<PromptStep> DefaultBatch(CommandContext c) { c.Editor.RunAfter("DEFAULT"); c.Editor.RunAfter(""); yield break; }
    [CommandMethod("LOOP", Summary = "Recursive probe", Syntax = "LOOP")]
    public IEnumerable<PromptStep> Loop(CommandContext c) { c.Editor.RunAfter("LOOP"); yield break; }
    [CommandMethod("INVALIDBATCH", Summary = "Invalid numeric probe", Syntax = "INVALIDBATCH")]
    public IEnumerable<PromptStep> InvalidBatch(CommandContext c) { c.Editor.RunAfter("REAL NaN"); c.Editor.RunAfter("LINE outer"); yield break; }
    [CommandMethod("BADBATCH", Summary = "Invalid script probe", Syntax = "BADBATCH")]
    public IEnumerable<PromptStep> BadBatch(CommandContext c) { c.Editor.RunAfter("NO_SUCH_COMMAND"); c.Editor.RunAfter("LINE outer"); yield break; }
}
