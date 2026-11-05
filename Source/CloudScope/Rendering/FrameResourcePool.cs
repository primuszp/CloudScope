namespace CloudScope.Rendering;

/// <summary>Reuses draw resources only after the owning GPU submission completes.</summary>
internal sealed class FrameResourcePool<T>(Func<int, T> create, Func<T, int> capacity, Action<T> release, long maximumCachedBytes = 64L * 1024 * 1024) : IDisposable
{
    private readonly List<T> _free = [];
    private List<T> _current = [];
    private readonly Queue<Submission> _submitted = new();
    private bool _active;
    public long CompletedSequence { get; private set; }
    public int InFlightCount => _submitted.Count;
    public long CachedBytes { get; private set; }
    public int CreatedResources { get; private set; }

    public void BeginFrame()
    {
        if (_active) throw new InvalidOperationException("A frame is already recording.");
        while (_submitted.TryPeek(out var frame) && (frame.IsComplete() || _submitted.Count >= 3)) RetireFirst();
        _active = true;
    }

    public T Rent(int bytes)
    {
        if (!_active) throw new InvalidOperationException("Draw resources require an active frame.");
        int best = -1;
        for (int i = 0; i < _free.Count; i++)
            if (capacity(_free[i]) >= bytes && (best < 0 || capacity(_free[i]) < capacity(_free[best]))) best = i;
        T resource;
        if (best < 0) { resource = create(bytes); CreatedResources++; }
        else { resource = _free[best]; _free.RemoveAt(best); CachedBytes -= capacity(resource); }
        _current.Add(resource);
        return resource;
    }

    public void Submit(long sequence, Func<bool> isComplete, Action wait, Action releaseCompletion)
    {
        if (!_active) throw new InvalidOperationException("No frame is recording.");
        _submitted.Enqueue(new(sequence, _current, isComplete, wait, releaseCompletion));
        _current = [];
        _active = false;
    }

    public void CancelFrame()
    {
        foreach (T resource in _current) Cache(resource);
        _current.Clear();
        _active = false;
    }

    private void RetireFirst()
    {
        var frame = _submitted.Dequeue();
        frame.Wait();
        foreach (T resource in frame.Resources) Cache(resource);
        CompletedSequence = frame.Sequence;
        frame.ReleaseCompletion();
    }

    private void Cache(T resource)
    {
        int bytes = capacity(resource);
        if (CachedBytes + bytes > maximumCachedBytes) release(resource);
        else { _free.Add(resource); CachedBytes += bytes; }
    }

    public void WaitForIdle() { while (_submitted.Count > 0) RetireFirst(); }
    public void Dispose()
    {
        WaitForIdle();
        CancelFrame();
        foreach (T resource in _free) release(resource);
        _free.Clear();
        CachedBytes = 0;
    }

    private sealed record Submission(long Sequence, List<T> Resources, Func<bool> IsComplete, Action Wait, Action ReleaseCompletion);
}
