using System.IO.Compression;
using System.Text.Json.Nodes;
using CloudScope;
using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Projects;
using CloudScope.Rendering;
using CloudScope.Store;
using CloudScope.Ui;
using OpenTK.Mathematics;

internal static class ProjectChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.xyz"), project = Path.Combine(directory, "work.cloudscope");
            File.WriteAllLines(source, from y in Enumerable.Range(0, 5)
                                       from x in Enumerable.Range(0, 5)
                                       select $"{1000 + x} {2000 + y} 3 128 64 255");
            using var viewer = NewViewer();
            var commands = new ViewerCommandDispatcher(viewer);
            commands.Execute($"OPENXYZ \"{source}\" 20"); Pump(viewer, () => viewer.LoadProgress < 0);
            commands.Execute("3DPOLY 0,0,0 1,1,1 2,1,0 Close");
            commands.Execute("PLINE 0,0,0 2,0,0 2,2,0 Close");
            viewer.CreateCrossSection(new(-1, 0, 0), new(2, 0, 0), 1.5f);
            viewer.SetViewportLayout(ViewportLayoutKind.Four, ViewportViewKind.Top);
            viewer.OrbitBy(13, 7); viewer.SaveNamedView("Inspection");
            viewer.SetPointSize(3); viewer.SetOrthoMode(true);
            viewer.LabelRegistry.Define("Survey", 42, new(.2f, .4f, .6f));
            string labels = Path.Combine(directory, "labels.json");
            File.WriteAllText(labels, "{\"labels\":{\"Survey\":[0,2]},\"instances\":{\"Survey\":{\"7\":[2]}}}");
            commands.Execute($"LOADLABELS Transfer \"{labels}\"");
            commands.Execute("THIN 80"); commands.Execute("COLORBY Class");
            viewer.ReconstructSurface(6, 4); Pump(viewer, () => viewer.Status.SurfaceProgress < 0);
            var expected = viewer.CaptureProject();
            check("Project fixture contains both drawing types, a section, annotations, views and a surface",
                expected.Polylines.Length == 1 && expected.PlanarPolylines.Length == 1 && expected.Sections.Length == 1
                && expected.Annotations.Length == 2 && expected.NamedViews.Count == 1 && expected.HasSurface, "");
            var result = commands.Execute($"SAVEPROJECT \"{project}\"");
            check("SAVEPROJECT atomically saves the complete document and checkpoint", result.Status == CommandStatus.Ended && File.Exists(project) && !viewer.HasUnsavedProject, result.Message);
            using (var zip = ZipFile.OpenRead(project))
            using (var reader = new StreamReader(zip.GetEntry("project.json")!.Open()))
            {
                var json = JsonNode.Parse(reader.ReadToEnd())!;
                check("External sources use relative references rather than copying the survey", json["Source"]!["Path"]!.GetValue<string>() == "source.xyz" && zip.GetEntry("resident.bin") == null, "");
            }
            using (var reopened = NewViewer())
            {
                reopened.OpenProject(project); Pump(reopened, () => reopened.LoadProgress < 0);
                check("Close/reopen restores all persisted project state exactly", ProjectFile.Serialize(reopened.CaptureProject()) == ProjectFile.Serialize(expected) && !reopened.HasUnsavedProject, "");
                check("Project source limit and source-coordinate frame survive reopening", reopened.Status.LoadedCount == 20 && reopened.CaptureProject().Source!.Identity.OriginX > 900, "");
                var edits = new ViewerCommandDispatcher(reopened);
                edits.Execute("3DPOLY 0,0,0 3,3,3"); edits.Execute("");
                check("A drawing edit dirties the complete project", reopened.HasUnsavedProject, "");
                edits.Execute("UNDO 1");
                check("Undo returns drawings to the project checkpoint", !reopened.HasUnsavedProject, "");
                edits.Execute("REDO 1");
                check("Redo leaves the project checkpoint", reopened.HasUnsavedProject, "");
                result = edits.Execute("RESET Cancel");
                check("Reset cancellation preserves unsaved drawings and the open source", result.Status == CommandStatus.Cancelled && reopened.Status.PolylineCount == 3 && reopened.Status.HasCloud, result.Message);
                result = edits.Execute("QUIT Yes");
                check("Quit saves to the existing project before requesting close", result.Status == CommandStatus.Ended && reopened.CloseRequested && !reopened.HasUnsavedProject, result.Message);
            }
            string before = ProjectFile.Serialize(viewer.CaptureProject());
            byte[] original = File.ReadAllBytes(project);
            bool protectedSource = false;
            try { viewer.SaveProject(source); } catch (InvalidOperationException) { protectedSource = true; }
            check("A project cannot overwrite its source survey", protectedSource && File.ReadAllText(source).StartsWith("1000"), "");
            bool writeFailed = false;
            viewer.OrbitBy(1, 0);
            try { viewer.SaveProject(Path.Combine(directory, "missing", "project.cloudscope")); } catch (IOException) { writeFailed = true; }
            check("Failed save preserves the old project, path and dirty state", writeFailed && viewer.HasUnsavedProject && viewer.ProjectPath == project && File.ReadAllBytes(project).SequenceEqual(original), "");
            viewer.OrbitBy(-1, 0);
            File.Move(source, source + ".away");
            string? error = null; viewer.BackgroundMessage += message => error = message;
            viewer.OpenProject(project); Pump(viewer, () => viewer.LoadProgress < 0);
            check("Missing source leaves the open document untouched", error?.Contains("failed") == true && ProjectFile.Serialize(viewer.CaptureProject()) == before, error ?? "");
            File.Move(source + ".away", source);
            string originalSource = File.ReadAllText(source);
            File.WriteAllText(source, originalSource.Replace("128 64 255", "0 64 255"));
            viewer.OpenProject(project); Pump(viewer, () => viewer.LoadProgress < 0);
            check("Changed RGB or attributes cannot silently change a saved project", error?.Contains("payload") == true && ProjectFile.Serialize(viewer.CaptureProject()) == before, error ?? "");
            File.WriteAllText(source, "0 0 0\n1 1 1\n");
            viewer.OpenProject(project); Pump(viewer, () => viewer.LoadProgress < 0);
            check("Changed source identity cannot apply labels to different points", error?.Contains("identity") == true && ProjectFile.Serialize(viewer.CaptureProject()) == before, error ?? "");

            string malformed = Path.Combine(directory, "bad.cloudscope");
            using (var zip = ZipFile.Open(project, ZipArchiveMode.Read))
            using (var reader = new StreamReader(zip.GetEntry("project.json")!.Open()))
            {
                var json = JsonNode.Parse(reader.ReadToEnd())!; json["Version"] = 999;
                using var bad = ZipFile.Open(malformed, ZipArchiveMode.Create);
                using var writer = new StreamWriter(bad.CreateEntry("project.json").Open()); writer.Write(json.ToJsonString());
            }
            viewer.OpenProject(malformed); Pump(viewer, () => viewer.LoadProgress < 0);
            check("Unknown project versions are rejected before changing the document", error?.Contains("version") == true && ProjectFile.Serialize(viewer.CaptureProject()) == before, error ?? "");

            File.Delete(malformed);
            using (var zip = ZipFile.Open(project, ZipArchiveMode.Read))
            using (var reader = new StreamReader(zip.GetEntry("project.json")!.Open()))
            {
                var json = JsonNode.Parse(reader.ReadToEnd())!; json.AsObject().Remove("Annotations");
                using var bad = ZipFile.Open(malformed, ZipArchiveMode.Create);
                using var writer = new StreamWriter(bad.CreateEntry("project.json").Open()); writer.Write(json.ToJsonString());
            }
            viewer.OpenProject(malformed); Pump(viewer, () => viewer.LoadProgress < 0);
            check("Missing required state is rejected instead of silently dropping annotations", error?.Contains("required", StringComparison.OrdinalIgnoreCase) == true && ProjectFile.Serialize(viewer.CaptureProject()) == before, error ?? "");

            EmbeddedAndRecovery(directory, check);
            Streaming(directory, check);
            Cancellation(directory, check);
            GpuFailure(directory, check);
            Relocation(directory, check);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void EmbeddedAndRecovery(string directory, Action<string, bool, string> check)
    {
        using var viewer = NewViewer();
        viewer.LoadPointCloud([new PointData { X = 1, R = 1 }, new PointData { Z = 3, B = .5f }], 4);
        viewer.BeginPolylineDraft(Vector3.Zero); viewer.AddPolylineDraftVertex(Vector3.One); viewer.CommitPolylineDraft(false);
        string path = Path.Combine(directory, "embedded.cloudscope");
        viewer.SaveProject(path);
        using var restored = NewViewer();
        restored.OpenProject(path); Pump(restored, () => restored.LoadProgress < 0);
        check("Generated geometry without a source path is embedded and restored", restored.Status.LoadedCount == 2 && restored.CaptureProject().Source!.Identity == viewer.CaptureProject().Source!.Identity && restored.Status.PolylineCount == 1, "");
        viewer.OrbitBy(10, 0);
        using var recovery = new ProjectRecovery(Path.Combine(directory, "recovery"));
        var state = viewer.CaptureProject();
        var dataset = ProjectFile.Read(path).Embedded;
        string? error = null;
        recovery.Tick(() => true, () => "changed", () => (state, dataset, null), message => error = message, force: true);
        recovery.Completion.GetAwaiter().GetResult();
        check("Recovery publishes a complete, independently readable snapshot", error == null && ProjectFile.Read(recovery.PathName).State.Workspace.Viewports[0].Camera == state.Workspace.Viewports[0].Camera, error ?? "");
        using (var recovered = NewViewer())
        {
            recovered.OpenProject(recovery.PathName, recovery: true); Pump(recovered, () => recovered.LoadProgress < 0);
            check("Recovered work requires an explicit project save and keeps the snapshot intact", recovered.HasUnsavedProject && recovered.ProjectPath == "" && File.Exists(recovery.PathName), "");
        }
        recovery.Clear();
        check("Explicit project save can clear the obsolete recovery snapshot", !File.Exists(recovery.PathName), "");
    }

    private static void Streaming(string directory, Action<string, bool, string> check)
    {
        string las = Path.Combine(directory, "source.las"); File.WriteAllBytes(las, LasChecks.Fixture(0, 2, 20));
        string first = Path.Combine(directory, "store a"), second = Path.Combine(directory, "store b");
        PointTileStoreBuilder.Build(las, first); PointTileStoreBuilder.Build(las, second);
        string project = Path.Combine(directory, "streaming.cloudscope");
        using (var viewer = NewViewer())
        {
            viewer.OpenPointTileStore(first);
            PointTileLabelFile.Save(first, [new PointRef(0, 0)], new Dictionary<int, PointAnnotation> { [0] = new("Tree", 4) });
            viewer.LoadLabels();
            viewer.OpenPointTileStore(second, replace: false);
            check("Adding a streamed layer preserves existing annotation references", viewer.CaptureProject().Annotations is [{ Layer: 0, Point: 0, Instance: 4 }], "");
            viewer.SetLayerVisible("store b", false);
            viewer.SaveProject(project);
        }
        using var restored = NewViewer();
        restored.OpenProject(project); Pump(restored, () => restored.LoadProgress < 0);
        check("Streaming projects restore layer identities, visibility and sparse labels", restored.Layers.Count == 2 && !restored.Layers[1].Visible && restored.LabelledPointCount == 1 && !restored.HasUnsavedProject, "");
        restored.CloseLayer("store a");
        check("Removing a layer removes its annotations without rebinding them to another survey", restored.LabelledPointCount == 0 && restored.CaptureProject().Layers.Length == 1 && restored.CaptureProject().Annotations.Length == 0, "");
    }

    private static void Cancellation(string directory, Action<string, bool, string> check)
    {
        string embedded = Path.Combine(directory, "embedded.cloudscope");
        using var viewer = NewViewer();
        viewer.LoadPointCloud([new PointData { X = 9 }]);
        string original = ProjectFile.Serialize(viewer.CaptureProject());
        viewer.OpenProject(embedded);
        viewer.ProjectCompletion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        viewer.CancelBackgroundOperations(); viewer.UpdateFrame(1f / 60, new ViewerKeyboardState());
        check("Cancellation discards a completed but unpublished project", original == ProjectFile.Serialize(viewer.CaptureProject()), "");
        viewer.OpenProject(embedded);
        viewer.ProjectCompletion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        viewer.BeginPolylineDraft(Vector3.Zero); viewer.AddPolylineDraftVertex(Vector3.One); viewer.CommitPolylineDraft(false);
        viewer.UpdateFrame(1f / 60, new ViewerKeyboardState());
        check("Edits made during project loading cannot be overwritten by a stale result", viewer.Status.LoadedCount == 1 && viewer.Status.PolylineCount == 1, "");
        var posted = new Queue<Action>();
        PreparedProject? abandoned = null;
        using var operation = new BackgroundOperation<PreparedProject>(posted.Enqueue, value => value.Dispose());
        operation.TryStart((cancel, progress) => abandoned = PreparedProject.Read(Path.Combine(directory, "streaming.cloudscope"), cancel, progress), _ => throw new InvalidOperationException("Unexpected publication"), _ => { });
        operation.Completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        var abandonedStore = abandoned!.Layers[0].Store;
        operation.Cancel(); posted.Dequeue()();
        bool disposed = false;
        try { abandonedStore.ReadPoints(0, new GpuPointVertex[1]); } catch (ObjectDisposedException) { disposed = true; }
        check("Cancelled project results dispose unpublished mapped tile stores", disposed && abandoned.Layers.Count == 0, "");
    }

    private static void GpuFailure(string directory, Action<string, bool, string> check)
    {
        var surface = new FailingSurface();
        using var viewer = new ViewerController(1280, 720, new HeadlessBackend(surface)); viewer.Load();
        viewer.LoadPointCloud([new PointData { X = 9 }]);
        string before = ProjectFile.Serialize(viewer.CaptureProject()); string? message = null;
        viewer.BackgroundMessage += value => message = value; surface.Fail = true;
        viewer.OpenProject(Path.Combine(directory, "embedded.cloudscope")); Pump(viewer, () => viewer.LoadProgress < 0);
        check("GPU publication failure restores the previous render data and preserves document state", message?.Contains("Injected GPU") == true && ProjectFile.Serialize(viewer.CaptureProject()) == before, message ?? "");
    }

    private static void Relocation(string directory, Action<string, bool, string> check)
    {
        string original = Path.Combine(directory, "portable"), moved = Path.Combine(directory, "relocated"); Directory.CreateDirectory(original);
        string source = Path.Combine(original, "survey.xyz"), path = Path.Combine(original, "project.cloudscope");
        File.WriteAllText(source, "1 2 3\n4 5 6\n");
        using (var viewer = NewViewer())
        { viewer.OpenXyzPointCloud(source); Pump(viewer, () => viewer.LoadProgress < 0); viewer.SaveProject(path); }
        Directory.Move(original, moved);
        using var restored = NewViewer();
        restored.OpenProject(Path.Combine(moved, "project.cloudscope")); Pump(restored, () => restored.LoadProgress < 0);
        check("Moving a project together with its relative sources preserves portability", restored.Status.LoadedCount == 2 && !restored.HasUnsavedProject && restored.Status.SourcePath == Path.Combine(moved, "survey.xyz"), "");
    }

    private sealed class FailingSurface : ISurfaceRenderer
    {
        public bool Fail;
        public void Upload(CloudScope.Loading.SurfaceMesh? mesh) { if (Fail) { Fail = false; throw new IOException("Injected GPU publication failure"); } }
        public void Render(IRenderFrameData frame, ref Matrix4 view, ref Matrix4 projection) { }
        public void Dispose() { }
    }
    private static ViewerController NewViewer()
    { var v = new ViewerController(1280, 720, new HeadlessBackend()); v.Load(); return v; }
    private static void Pump(ViewerController viewer, Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do { viewer.UpdateFrame(1f / 60, new ViewerKeyboardState()); if (done()) return; Thread.Sleep(2); }
        while (timer.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("Project operation did not complete.");
    }
}
