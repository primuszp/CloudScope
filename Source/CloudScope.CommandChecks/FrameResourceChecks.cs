using CloudScope.Rendering;

internal static class FrameResourceChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        int created = 0, released = 0, waits = 0;
        using var pool = new FrameResourcePool<Resource>(size => new(++created, size), r => r.Bytes, _ => released++);
        var completions = Enumerable.Range(0, 4).Select(_ => new Completion()).ToArray();
        pool.BeginFrame();
        var first = Enumerable.Range(0, 600).Select(_ => pool.Rent(16)).ToArray();
        check("More than 512 draws keep distinct geometry storage", first.Distinct().Count() == 600, "");
        Submit(0);
        pool.BeginFrame();
        Resource second = pool.Rent(16);
        check("Incomplete submissions never share draw storage", !first.Contains(second), "");
        Submit(1);
        pool.BeginFrame(); pool.Rent(16); Submit(2);
        pool.BeginFrame();
        check("In-flight bound waits for actual GPU completion", waits == 1 && pool.CompletedSequence == 1, "");
        check("Completed submissions recycle their storage", first.Contains(pool.Rent(16)), "");
        Submit(3);
        pool.Dispose();
        check("Frame pool shutdown releases every native resource exactly once", created == released && pool.InFlightCount == 0, $"{created}/{released}");
        void Submit(int i) => pool.Submit(i + 1, () => completions[i].Done,
            () => { if (!completions[i].Done) waits++; completions[i].Done = true; }, () => { });
    }
    private sealed record Resource(int Id, int Bytes);
    private sealed class Completion { public bool Done; }
}
