using System.Collections.Concurrent;
using CloudScope;
using CloudScope.Loading;
using CloudScope.Rendering;
using OpenTK.Mathematics;

internal static class BackgroundOperationChecks
{
    public static async Task Run(Action<string, bool, string> check)
    {
        void Verify(string name, bool ok) => check(name, ok, "");
        var queue = new ConcurrentQueue<Action>();
        var published = new List<int>();
        var errors = new List<Exception>();
        using var operation = new BackgroundOperation<int>(queue.Enqueue);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelled = false;
        operation.TryStart((token, progress) =>
        {
            progress.Report(25);
            started.SetResult();
            try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return 1;
        }, published.Add, errors.Add);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Verify("Active operation exposes only its own progress", operation.Progress == 25);
        Verify("Busy operation refuses a second worker", !operation.TryStart((_, _) => 99, published.Add, errors.Add));
        operation.Cancel();
        Verify("Cancellation immediately clears running state and progress", !operation.IsRunning && operation.Progress == -1);
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Verify("Cancellation reaches the worker and suppresses obsolete output", cancelled && queue.IsEmpty && published.Count == 0 && errors.Count == 0);

        IProgress<int>? obsoleteProgress = null;
        operation.TryStart((_, progress) => { obsoleteProgress = progress; return 1; }, published.Add, errors.Add);
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Verify("Completed worker awaits viewer-thread publication", operation.IsRunning && queue.Count == 1 && published.Count == 0);
        operation.Cancel();
        obsoleteProgress!.Report(90);
        Verify("Late progress cannot resurrect a cancelled operation", operation.Progress == -1);
        operation.TryStart((_, progress) => { progress.Report(50); return 2; }, published.Add, errors.Add);
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Drain();
        Verify("Only the latest operation publishes after replacement", published.SequenceEqual([2]) && !operation.IsRunning && operation.Progress == -1);

        operation.TryStart((_, _) => throw new InvalidDataException("worker failure"), published.Add, errors.Add);
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Drain();
        Verify("Worker failure is delivered on the viewer thread and leaves an idle operation", errors.Count == 1 && !operation.IsRunning);
        operation.TryStart((_, _) => 3, _ => throw new InvalidOperationException("upload failure"), errors.Add);
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Drain();
        Verify("Publication failure is reported without wedging the operation", errors.Count == 2 && errors[^1].Message == "upload failure" && !operation.IsRunning);

        var disposing = new BackgroundOperation<int>(queue.Enqueue);
        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool disposeCancelled = false;
        disposing.TryStart((token, progress) =>
        {
            disposeStarted.SetResult();
            token.WaitHandle.WaitOne();
            disposeCancelled = token.IsCancellationRequested;
            progress.Report(100);
            return 4;
        }, published.Add, errors.Add);
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        disposing.Dispose(); disposing.Dispose();
        await disposing.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Drain();
        Verify("Disposal cancels work and suppresses progress/publication", disposeCancelled && disposing.Progress == -1 && published.SequenceEqual([2]));
        bool closed = false;
        try { disposing.TryStart((_, _) => 5, published.Add, errors.Add); }
        catch (ObjectDisposedException) { closed = true; }
        Verify("Disposed operation rejects new workers", closed);

        var surface = new UploadProbe();
        using var viewer = new ViewerController(1280, 720, new HeadlessBackend(surface));
        viewer.Load();
        var points = (from y in Enumerable.Range(0, 5) from x in Enumerable.Range(0, 5)
            select new PointData { X = x, Y = y }).ToArray();
        viewer.LoadPointCloud(new PointCloudDataset(points, points.Length, 10, false, 1,
            new PointCloudAttributes(new byte[points.Length], new ushort[points.Length], new byte[points.Length], new double[points.Length], 0, 0)));
        viewer.ReconstructSurface(8, 1.5f);
        viewer.ClearSurface();
        Verify("Surface Clear resets progress immediately", viewer.Status.SurfaceProgress == -1);
        Verify("Reconstruction can restart before obsolete work drains", viewer.ReconstructSurface(8, 1.5f) == "Reconstructing surface...");
        var messages = new List<string>();
        viewer.BackgroundMessage += messages.Add;
        var keyboard = new ViewerKeyboardState();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (viewer.Status.SurfaceProgress >= 0)
        {
            viewer.UpdateFrame(1f / 60, keyboard);
            if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Surface publication stalled.");
            await Task.Delay(1);
        }
        Verify("Upload failure keeps the viewer usable and surface unpublished", viewer.Status.SurfaceTriangles == 0 && messages.Any(m => m.Contains("upload failure")));
        Verify("Viewer can retry reconstruction after an upload failure", viewer.ReconstructSurface(8, 1.5f) == "Reconstructing surface...");
        timeout.Restart();
        while (viewer.Status.SurfaceProgress >= 0)
        {
            viewer.UpdateFrame(1f / 60, keyboard);
            if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Surface retry stalled.");
            await Task.Delay(1);
        }
        Verify("Retried reconstruction publishes exactly once", viewer.Status.SurfaceTriangles > 0 && surface.Published == 1);
        var dispatcher = new CloudScope.Ui.ViewerCommandDispatcher(viewer);
        viewer.Dispose();
        Verify("Late frame callbacks close safely after viewer disposal", viewer.UpdateFrame(1f / 60, keyboard));
        viewer.RenderFrame(1d / 60); viewer.Resize(1280, 720);
        Verify("Closed viewers reject commands before reaching renderer resources", dispatcher.Execute("POINTSIZE 4").Status == CloudScope.Commands.CommandStatus.Failed);
        Verify("Closed viewers cannot restart HTTP listeners", viewer.ConfigureLocalApi(true) == "The viewer is closed.");

        void Drain() { while (queue.TryDequeue(out var action)) action(); }
    }

    private sealed class UploadProbe : ISurfaceRenderer
    {
        private bool _fail = true;
        public int Published { get; private set; }
        public void Upload(SurfaceMesh? mesh)
        {
            if (mesh == null) return;
            if (_fail) { _fail = false; throw new InvalidOperationException("upload failure"); }
            Published++;
        }
        public void Render(IRenderFrameData frame, ref Matrix4 view, ref Matrix4 projection) { }
        public void Dispose() { }
    }
}
