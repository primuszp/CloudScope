using System.Buffers;
using CloudScope;
using CloudScope.Rendering;

internal static class StreamingChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        var source = new Source();
        var pages = new PointTilePageTable(1, 4);
        var buffer = new Buffer();
        using (var resident = new PointTileResidency(source, buffer, pages))
        {
            Pump(resident, [new(0, 0, 0), new(0, 1, 0)], () => resident.LoadError != null && resident.ResidentCellCount == 1);
            check("A failed cell reports its error while the next cell still loads", resident.LoadError!.Contains("Injected read") && buffer.Writes == 1, "");
            check("Failed cells leave the pending queue", resident.PendingCellCount == 0, "");
            for (int i = 0; i < 10; i++) Draw(resident, [new(0, 0, 0), new(0, 1, 0)]);
            check("A corrupt cell does not retry forever", source.Reads == 2, source.Reads.ToString());
            check("Uploaded cell arrays return to their pool", source.Last!.IsDisposed, "");
        }
        check("Streaming disposal returns every page", pages.FreePageCount == 4, "");
        using (var resident = new PointTileResidency(new Source { FailReads = false }, new Buffer { Fail = true }, pages))
        {
            Pump(resident, [new(0, 1, 0)], () => resident.LoadError != null);
            check("Failed uploads release their page allocation", pages.FreePageCount == 4 && resident.ResidentCellCount == 0, "");
        }
        var blocked = new Source { FailReads = false, Block = true };
        var pending = new PointTileResidency(blocked, buffer, pages);
        try
        {
            Draw(pending, [new(0, 1, 0)]);
            if (!blocked.Entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Reader did not start.");
            pending.Dispose();
            check("Shutdown joins the reader and disposes an unpublished result", blocked.Exited.IsSet && blocked.Last!.IsDisposed && pending.PendingCellCount == 0, "");
            pending.Dispose();
        }
        finally { pending.Dispose(); }
    }

    private static void Draw(PointTileResidency r, PointTileVisit[] visits)
        => r.BeginFrame(visits, new PointDrawRange[8], new PointTileLayerBatch[1], out _);

    private static void Pump(PointTileResidency r, PointTileVisit[] visits, Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Draw(r, visits);
            if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Streaming did not settle.");
            Thread.Yield();
        }
    }

    private sealed class Source : IPointTileCellSource
    {
        public int LayerCount => 1;
        public bool IsVisible(int layer) => true;
        public int Reads;
        public bool FailReads = true, Block;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Exited { get; } = new();
        public PointTileCellData? Last;
        public PointTileCellData Read(PointTileCellKey key, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Reads);
            Entered.Set();
            if (Block) cancellation.WaitHandle.WaitOne();
            if (FailReads && key.NodeIndex == 0) throw new IOException("Injected read failure");
            Last = new PointTileCellData(1, ArrayPool<GpuPointVertex>.Shared.Rent(1));
            Exited.Set();
            return Last;
        }
    }
    private sealed class Buffer : IPointTilePageBuffer
    {
        public int Writes;
        public bool Fail;
        public void WritePage(int firstPoint, ReadOnlySpan<GpuPointVertex> points, ReadOnlySpan<GpuPointAttribute> attributes)
        {
            if (Fail) throw new IOException("Injected upload failure");
            Writes++;
        }
    }
}
