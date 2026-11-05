using CloudScope;
using CloudScope.Forestry;
using CloudScope.Loading;
using CloudScope.Store;
using CloudScope.Ui;
using OpenTK.Mathematics;

internal static class ProcessingChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        PointData[] points = (from y in Enumerable.Range(0, 8) from x in Enumerable.Range(0, 8)
            select new PointData { X = x, Y = y, Z = 0 }).ToArray();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        bool groundStopped = false, treeStopped = false;
        try { GroundSegmentation.Segment(points, cancellation: stop.Token); } catch (OperationCanceledException) { groundStopped = true; }
        try { TreeSegmentation.Segment(points, Vector3.Zero, cancellation: stop.Token); } catch (OperationCanceledException) { treeStopped = true; }
        check("Both forestry algorithms honor cancellation before work", groundStopped && treeStopped, "");
        using var mid = new CancellationTokenSource();
        bool midStopped = false;
        try { GroundSegmentation.Segment(points, cancellation: mid.Token, progress: new Progress(p => { if (p >= 10) mid.Cancel(); })); }
        catch (OperationCanceledException) { midStopped = true; }
        check("CSF cancels during cloth simulation", midStopped, "");
        using var viewer = new ViewerController(640, 480, new HeadlessBackend());
        viewer.Load();
        Load();
        var commands = new ViewerCommandDispatcher(viewer);
        viewer.SegmentGround();
        viewer.ProcessingCompletion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        check("Segmentation cannot mutate labels on its worker thread", viewer.LabelledPointCount == 0, "");
        viewer.UndoHistory.BeginMark("Foreground drawing");
        viewer.UpdateFrame(1f / 60, new ViewerKeyboardState());
        viewer.UndoHistory.Rollback();
        check("Background labels publish as a separate undo transaction", viewer.LabelledPointCount > 0 && viewer.UndoHistory.NextUndoName == "GROUNDSEG", "");
        commands.Execute("UNDO 1");
        check("An asynchronous segmentation result undoes in one step", viewer.LabelledPointCount == 0, "");
        viewer.SegmentGround();
        viewer.ProcessingCompletion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        viewer.Reset();
        viewer.UpdateFrame(1f / 60, new ViewerKeyboardState());
        check("Source reset discards a completed but unpublished segmentation", viewer.LabelledPointCount == 0 && viewer.ProcessingProgress < 0, "");
        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-index-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string las = Path.Combine(directory, "source.las"), target = Path.Combine(directory, "store");
            File.WriteAllBytes(las, LasChecks.Fixture(0, 2, 20));
            using var cancel = new CancellationTokenSource();
            bool indexStopped = false;
            try { PointTileStoreBuilder.Build(las, target, progress: new IndexProgress(_ => cancel.Cancel()), cancellation: cancel.Token); }
            catch (OperationCanceledException) { indexStopped = true; }
            check("Cancelled indexing publishes no partial store", indexStopped && !Directory.Exists(target) && Directory.GetDirectories(directory).Length == 0, "");
            var report = PointTileStoreBuilder.Build(las, target);
            using (var store = PointTileStore.Open(target))
                check("A completed staged index reopens with the correct point count", report.StoredPoints == 1 && store.Header.PointCount == 1, "");
            bool protectedStore = false;
            try { PointTileStoreBuilder.Build(las, target); } catch (IOException) { protectedStore = true; }
            check("Indexing never overwrites an existing store", protectedStore && File.Exists(Path.Combine(target, "index.bin")), "");
            string asyncTarget = Path.Combine(directory, "background-store");
            viewer.IndexPointCloud(las, asyncTarget, PointTileBuildOptions.Default);
            viewer.ProcessingCompletion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            viewer.UpdateFrame(1f / 60, new ViewerKeyboardState());
            check("Viewer indexing completes through the common background service", Directory.Exists(asyncTarget) && viewer.ProcessingProgress < 0, "");
        }
        finally { Directory.Delete(directory, recursive: true); }
        void Load() => viewer.LoadPointCloud(new PointCloudDataset(points, points.Length, 10, false, 1,
            new PointCloudAttributes(new byte[points.Length], new ushort[points.Length], new byte[points.Length], new double[points.Length], 0, 0)));
    }
    private sealed class Progress(Action<int> action) : IProgress<int> { public void Report(int value) => action(value); }
    private sealed class IndexProgress(Action<PointTileBuildProgress> action) : IProgress<PointTileBuildProgress> { public void Report(PointTileBuildProgress value) => action(value); }
}
