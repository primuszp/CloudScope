using CloudScope;
using CloudScope.Commands;
using CloudScope.Loading;
using CloudScope.Ui;

internal static class ViewerWorkflowChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "cloud with spaces.xyz");
            File.WriteAllLines(path, from y in Enumerable.Range(0, 5) from x in Enumerable.Range(0, 5) select $"{x} {y} 0");
            using var viewer = new ViewerController(1280, 720, new HeadlessBackend());
            viewer.Load();
            var dispatcher = new ViewerCommandDispatcher(viewer);
            var keyboard = new ViewerKeyboardState();
            var result = dispatcher.Execute($"OPENXYZ \"{path}\" 7");
            check("OPENXYZ accepts quoted path and point limit in one command", result.Status == CommandStatus.Ended && result.Message.Contains("Reading"), result.Message);
            PumpUntil(() => viewer.LoadProgress < 0);
            check("Resident background import respects the requested limit", viewer.Status.LoadedCount == 7, viewer.Status.LoadedCount.ToString());
            result = dispatcher.Execute($"OPENXYZ \"{path}\" 0");
            check("Explicit zero imports all points without another prompt", result.Status == CommandStatus.Ended, result.Message);
            PumpUntil(() => viewer.LoadProgress < 0);
            check("Resident background import publishes the full cloud", viewer.Status.LoadedCount == 25, "");
            string labels = Path.Combine(directory, "labels.json");
            File.WriteAllText(labels, "{\"labels\":{\"Tree\":[0,1]}}");
            dispatcher.Execute($"LOADLABELS Transfer \"{labels}\"");
            check("Text-cloud JSON label import applies source indices", viewer.LabelledPointCount == 2, "");
            string savedLabels = Path.Combine(directory, "saved labels.json");
            result = dispatcher.Execute($"SAVELABELS \"{savedLabels}\"");
            check("SAVELABELS accepts a direct path for a non-LAS source", result.Status == CommandStatus.Ended && File.Exists(savedLabels), result.Message);
            dispatcher.Execute("UNDO 1");
            check("LOADLABELS is one reversible command", viewer.LabelledPointCount == 0, "");
            dispatcher.Execute("REDO 1");
            check("LOADLABELS can be redone", viewer.LabelledPointCount == 2, "");
            dispatcher.Execute($"OPENXYZ \"{path}\" 25 No");
            PumpUntil(() => viewer.LoadProgress < 0);
            check("New resident cloud clears prior source labels and command history", viewer.LabelledPointCount == 0 && !viewer.UndoHistory.CanUndo, "");
            dispatcher.Execute("RECONSTRUCT 8 1.5");
            PumpUntil(() => viewer.Status.SurfaceTriangles > 0);
            string obj = Path.Combine(directory, "surface.obj");
            result = dispatcher.Execute($"EXPORTOBJ \"{obj}\"");
            check("RECONSTRUCT and EXPORTOBJ complete through real commands", result.Status == CommandStatus.Ended && File.Exists(obj) && File.ReadAllText(obj).Contains("\nf "), result.Message);
            string ply = Path.Combine(directory, "filtered.ply");
            dispatcher.Execute("THIN 40");
            result = dispatcher.Execute($"EXPORT PlyBinary \"{ply}\"");
            check("THIN and EXPORT preserve the source identity and exact sample count", result.Status == CommandStatus.Ended && PlyPointCloudLoader.Load(ply).LoadedCount == 10, result.Message);
            check("Changing geometry invalidates the previous surface", viewer.Status.SurfaceTriangles == 0, "");
            // Hold the completed upload in the frame queue, then reset before it is applied.
            dispatcher.Execute($"OPENXYZ \"{path}\" 25 No");
            WaitUntil(() => viewer.HasPendingFrameWork);
            viewer.Reset();
            viewer.UpdateFrame(1f / 60, keyboard);
            check("RESET rejects stale background upload", !viewer.Status.HasCloud && viewer.LoadProgress == -1, "");

            void PumpUntil(Func<bool> done) => WaitUntil(() => { viewer.UpdateFrame(1f / 60, keyboard); return done(); });
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void WaitUntil(Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Viewer workflow did not complete.");
            Thread.Sleep(5);
        }
    }
}
