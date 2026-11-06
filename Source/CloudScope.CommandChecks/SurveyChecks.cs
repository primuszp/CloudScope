using System.Buffers.Binary;
using CloudScope;
using CloudScope.Projects;
using CloudScope.Rendering;
using CloudScope.Store;
using OpenTK.Mathematics;

internal static class SurveyChecks
{
    public static void Run(Action<string,bool,string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(),"cloudscope-survey-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory,"source.las"); File.WriteAllBytes(path,Fixture());
            var report = SurveyBenchmark.Run(path,Path.Combine(directory,"result"));
            check("Survey benchmark verifies its indexed selection against a full independent point-file scan",
                report.Points == 1000 && report.SelectedPoints > 0 && report.IndexedSelectionMatchesFullScan, "");
            using var store = PointTileStore.Open(report.Store);
            var all = new GpuPointVertex[1000]; store.ReadPoints(0,all);
            long[] requested = [.. Enumerable.Range(0,100).Select(i => (long)i),600,3,3,4,5,999];
            var refs = requested.Select(index => new PointRef(0,index)).ToArray();
            var positions = PointTileVolumeQuery.ReadPositions(store,refs);
            check("Batched position readback preserves contiguous runs, gaps, duplicates and caller order", positions.Select((p,i) =>
                p == new Vector3(all[requested[i]].X,all[requested[i]].Y,all[requested[i]].Z)).All(equal => equal), "");
            bool cancelled = false;
            try { PointTileVolumeQuery.ReadPositions(store,refs,new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
            check("Position readback honors cancellation before allocating its result", cancelled, "");
            bool overflow = false, bounds = false;
            try { store.ReadPoints(long.MaxValue,new GpuPointVertex[2]); } catch (ArgumentOutOfRangeException) { overflow = true; }
            try { store.ReadPoints(999,new GpuPointVertex[2]); } catch (ArgumentOutOfRangeException) { bounds = true; }
            check("Mapped point reads reject overflowing and out-of-range indices", overflow && bounds, "");
            check("Survey measurements record the actual runtime and memory/time samples", report.Operations.Length >= 8
                && report.Operations.All(o => o.Milliseconds >= 0 && o.AllocatedBytes >= 0 && o.PeakWorkingSetBytes > 0), "");
            string fragmented = Path.Combine(directory,"fragmented");
            PointTileStoreBuilder.Build(path,fragmented,PointTileBuildOptions.Default with { CellGridResolution=2, MinimumCellPoints=10 });
            using var fragmentedStore = PointTileStore.Open(fragmented);
            var layers = new[] { new PointTileLayer("Fragmented",fragmentedStore) };
            var camera = SurveyBenchmark.Camera(fragmentedStore);
            var view = new PointRenderView(camera.GetViewMatrix(),camera.GetProjectionMatrix(),1920,1080,2);
            var visits = new PointTileVisit[fragmentedStore.Nodes.Length];
            int unbounded = PointTileTraversal.Collect(layers,view,1000,visits,new PointTileTraversalScratch(),out _);
            int bounded = PointTileTraversal.Collect(layers,view,1000,visits,new PointTileTraversalScratch(),out _,residentPageBudget:2);
            check("Streaming traversal budgets whole pages as well as point count", unbounded > bounded && bounded > 0
                && visits.Take(bounded).Sum(v => (fragmentedStore.Nodes[v.NodeIndex].PointCount+65535)/65536) <= 2, "");
            var pages = new PointTilePageTable(65536,2);
            using var resident = new PointTileResidency(layers,new PageSink(),pages);
            var ranges = new PointDrawRange[2]; var batches = new PointTileLayerBatch[1];
            var timer = System.Diagnostics.Stopwatch.StartNew();
            long sequence = 0, drawn;
            do
            {
                resident.PrepareFrame(++sequence,sequence-1);
                resident.BeginFrame(visits.AsSpan(0,bounded),ranges,batches,out drawn);
                if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Page-bounded residency did not settle.");
                Thread.Sleep(1);
            } while (resident.PendingCellCount != 0 || drawn == 0);
            check("A fragmented visible set settles within its page budget instead of loading forever", resident.ResidentCellCount <= 2
                && resident.PendingCellCount == 0 && drawn > 0, "");
        }
        finally { Directory.Delete(directory,true); }
    }

    private sealed class PageSink : IPointTilePageBuffer
    {
        public void WritePage(int firstPoint, ReadOnlySpan<GpuPointVertex> points, ReadOnlySpan<GpuPointAttribute> attributes) { }
    }

    private static byte[] Fixture()
    {
        const int count = 1000, header = 227, stride = 20;
        var one = LasChecks.Fixture(0,2,stride); var bytes = new byte[header+count*stride]; one.AsSpan(0,header).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(107),count);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(111),count);
        for (int axis = 0; axis < 3; axis++)
        {
            double min = (axis+1)*1001;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(179+16*axis), min+(count-1)*(axis+1)*.01);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(187+16*axis), min);
        }
        for (int i = 0; i < count; i++)
        {
            one.AsSpan(header,stride).CopyTo(bytes.AsSpan(header+i*stride));
            for (int axis = 0; axis < 3; axis++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header+i*stride+axis*4),(axis+1)*(100+i));
        }
        return bytes;
    }
}
