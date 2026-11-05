namespace CloudScope;

/// <summary>One cancellable CPU job, with publication and messages confined to the viewer thread.</summary>
internal sealed class ViewerProcessingService(Action<Action> post, Action<string> message) : IDisposable
{
    private readonly BackgroundOperation<Action> _operation = new(post);
    public string Name { get; private set; } = "";
    public int Progress => _operation.Progress;
    public bool IsRunning => _operation.IsRunning;
    public bool RequiresDocument { get; private set; }
    internal Task Completion => _operation.Completion;

    public bool Start<T>(string name, Func<CancellationToken, IProgress<int>, T> work, Action<T> publish, bool requiresDocument = true)
    {
        if (!_operation.TryStart((cancellation, progress) =>
        {
            T result = work(cancellation, progress);
            return () => publish(result);
        }, action => { Name = ""; action(); }, error => { Name = ""; message($"{name} failed: {error.Message}"); })) return false;
        Name = name;
        RequiresDocument = requiresDocument;
        return true;
    }
    public void CancelDocumentWork() { if (RequiresDocument) Cancel(); }
    public void Cancel() { _operation.Cancel(); Name = ""; }
    public void Dispose() => _operation.Dispose();
}
