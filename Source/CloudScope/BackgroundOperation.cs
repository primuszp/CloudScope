namespace CloudScope;

/// <summary>
/// Owns one replaceable background operation. Progress and publication belong to the same
/// run; cancellation makes a run obsolete immediately, even if its worker is still exiting.
/// Graphics resources are touched only by a completion posted to the viewer thread.
/// </summary>
internal sealed class BackgroundOperation<T>(Action<Action> post, Action<T>? discard = null) : IDisposable
{
    private readonly object _gate = new();
    private Run? _active;
    private bool _disposed;
    private Task _completion = Task.CompletedTask;

    public int Progress { get { lock (_gate) return _active?.Progress ?? -1; } }
    public bool IsRunning { get { lock (_gate) return _active != null; } }
    public Task Completion { get { lock (_gate) return _completion; } }

    public bool TryStart(Func<CancellationToken, IProgress<int>, T> work,
        Action<T> publish, Action<Exception> failed)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active != null) return false;
            var run = new Run();
            _active = run;
            _completion = Task.Run(() => Execute(run, work, publish, failed));
            return true;
        }
    }

    private void Execute(Run run, Func<CancellationToken, IProgress<int>, T> work,
        Action<T> publish, Action<Exception> failed)
    {
        T result = default!;
        Exception? error = null;
        try
        {
            run.Cancellation.Token.ThrowIfCancellationRequested();
            result = work(run.Cancellation.Token, new OperationProgress(value =>
            {
                lock (_gate)
                    if (ReferenceEquals(_active, run)) run.Progress = Math.Clamp(value, 0, 100);
            }));
        }
        catch (Exception ex) { error = ex; }
        finally
        {
            lock (_gate)
            {
                run.Finished = true;
                run.Cancellation.Dispose();
            }
        }

        lock (_gate)
            if (!ReferenceEquals(_active, run)) { if (error == null) discard?.Invoke(result); return; }
        post(() =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_active, run)) { if (error == null) discard?.Invoke(result); return; }
                _active = null;
            }
            if (error != null) failed(error);
            else
            {
                try { publish(result); }
                catch (Exception ex) { failed(ex); }
            }
        });
    }

    public void Cancel()
    {
        lock (_gate) CancelCore();
    }

    private void CancelCore()
    {
        Run? run = _active;
        _active = null;
        if (run is { Finished: false }) run.Cancellation.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelCore();
        }
    }

    private sealed class Run
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public int Progress;
        public bool Finished;
    }

    private sealed class OperationProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
