using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CloudScope.Library;

namespace CloudScope.Store;

/// <summary>Progress of a store build, in the order the stages run.</summary>
public enum PointTileBuildStage
{
    Surveying,
    Distributing,
    BuildingCells,
    Writing
}

public readonly record struct PointTileBuildProgress(PointTileBuildStage Stage, long Done, long Total);

/// <summary>Knobs a build can be tuned with; the defaults suit a workstation.</summary>
public readonly record struct PointTileBuildOptions
{
    /// <summary>Points a chunk aims for, and so the working set of one build thread.</summary>
    public int ChunkTargetPoints { get; init; }

    /// <summary>Resolution of the sampling grid used at every octree cell.</summary>
    public int CellGridResolution { get; init; }

    /// <summary>A cell holding no more than this keeps all of its points and stops splitting.</summary>
    public int MinimumCellPoints { get; init; }

    /// <summary>Directory for the intermediate chunk files; defaults to the output directory.</summary>
    public string? ScratchDirectory { get; init; }

    public static PointTileBuildOptions Default => new()
    {
        ChunkTargetPoints = 8_000_000,
        CellGridResolution = 64,
        MinimumCellPoints = 50_000,
        ScratchDirectory = null
    };
}

/// <summary>
/// Converts a LAS file of any size into a <see cref="PointTileStore"/>, using an amount of
/// memory that depends on the chunk size rather than on the size of the file.
/// </summary>
/// <remarks>
/// Three passes, which is what keeps the memory bounded:
/// <list type="number">
/// <item><b>Survey</b> counts the points falling into each cell of a coarse grid. The counts
/// are a few megabytes whatever the file holds, and they say where the cloud actually is -
/// a survey cloud fills a tiny fraction of its bounding box.</item>
/// <item><b>Distribute</b> streams the points a second time and appends each to the chunk file
/// its region belongs to. Chunks are sized from the survey counts, so each one fits in
/// memory even where the cloud is dense.</item>
/// <item><b>Build cells</b> loads one chunk at a time - several at once, one per core - and
/// turns it into octree cells, which are appended to the store.</item>
/// </list>
/// Cells are additive: a point is kept by the coarsest cell whose sampling grid still had its
/// square free, and only the rest are pushed to the children. No point is stored twice, and
/// stopping a traversal at any depth leaves an even sample of the region.
/// </remarks>
public static class PointTileStoreBuilder
{
    /// <summary>Resolution of the survey grid, per axis.</summary>
    private const int SurveyResolution = 128;

    private const int SurveyCellCount = SurveyResolution * SurveyResolution * SurveyResolution;
    private const int ReadBatchPoints = 1 << 18;

    public static PointTileBuildReport Build(
        string lasPath,
        string outputDirectory,
        PointTileBuildOptions options = default,
        IProgress<PointTileBuildProgress>? progress = null)
    {
        if (options.ChunkTargetPoints <= 0)
            options = PointTileBuildOptions.Default;

        Directory.CreateDirectory(outputDirectory);
        string scratch = options.ScratchDirectory ?? Path.Combine(outputDirectory, "chunks");
        Directory.CreateDirectory(scratch);

        var total = Stopwatch.StartNew();
        using var reader = new LasReader(lasPath);
        LasHeader header = reader.Header;
        long pointCount = reader.PointCount;

        var bounds = CloudBounds.FromHeader(header);
        var sourceReader = new LasPointStream(reader, bounds);

        var stage = Stopwatch.StartNew();
        long[] surveyCounts = Survey(sourceReader, pointCount, progress);
        TimeSpan surveyTime = stage.Elapsed;

        stage.Restart();
        ChunkPlan plan = ChunkPlan.FromSurvey(surveyCounts, options.ChunkTargetPoints);
        long[] chunkCounts = Distribute(sourceReader, pointCount, plan, scratch, progress);
        TimeSpan distributeTime = stage.Elapsed;

        stage.Restart();
        long written;
        int nodeCount;
        int maxLevel;
        TimeSpan cellTime;
        TimeSpan writeTime;
        using (var writer = new PointTileStoreWriter(outputDirectory, sourceReader.HasAttributes))
        {
            written = BuildCells(plan, chunkCounts, scratch, bounds, options, writer, progress);
            cellTime = stage.Elapsed;

            stage.Restart();
            progress?.Report(new PointTileBuildProgress(PointTileBuildStage.Writing, 0, writer.NodeCount));
            writer.Complete(bounds, pointCount);
            writeTime = stage.Elapsed;
            nodeCount = writer.NodeCount;
            maxLevel = writer.MaxLevel;
        }

        Directory.Delete(scratch, recursive: true);

        return new PointTileBuildReport(
            pointCount, written, nodeCount, maxLevel, plan.ChunkCount,
            surveyTime, distributeTime, cellTime, writeTime, total.Elapsed);
    }

    private static long[] Survey(LasPointStream source, long pointCount, IProgress<PointTileBuildProgress>? progress)
    {
        var counts = new long[SurveyCellCount];
        var batch = new StorePoint[ReadBatchPoints];
        long done = 0;
        while (done < pointCount)
        {
            int read = source.Read(done, batch);
            if (read == 0)
                break;

            for (int i = 0; i < read; i++)
                counts[source.SurveyCell(batch[i])]++;

            done += read;
            progress?.Report(new PointTileBuildProgress(PointTileBuildStage.Surveying, done, pointCount));
        }

        return counts;
    }

    private static long[] Distribute(
        LasPointStream source,
        long pointCount,
        ChunkPlan plan,
        string scratchDirectory,
        IProgress<PointTileBuildProgress>? progress)
    {
        var writers = new ChunkWriter[plan.ChunkCount];
        for (int i = 0; i < writers.Length; i++)
            writers[i] = new ChunkWriter(Path.Combine(scratchDirectory, $"chunk{i:D5}.bin"));

        try
        {
            var batch = new StorePoint[ReadBatchPoints];
            long done = 0;
            while (done < pointCount)
            {
                int read = source.Read(done, batch);
                if (read == 0)
                    break;

                for (int i = 0; i < read; i++)
                    writers[plan.ChunkOf(source.SurveyCell(batch[i]))].Add(batch[i]);

                done += read;
                progress?.Report(new PointTileBuildProgress(PointTileBuildStage.Distributing, done, pointCount));
            }

            var counts = new long[writers.Length];
            for (int i = 0; i < writers.Length; i++)
                counts[i] = writers[i].Count;
            return counts;
        }
        finally
        {
            foreach (ChunkWriter writer in writers)
                writer.Dispose();
        }
    }

    private static long BuildCells(
        ChunkPlan plan,
        long[] chunkCounts,
        string scratchDirectory,
        CloudBounds bounds,
        PointTileBuildOptions options,
        PointTileStoreWriter writer,
        IProgress<PointTileBuildProgress>? progress)
    {
        long done = 0;
        long total = 0;
        foreach (long count in chunkCounts)
            total += count;

        // One chunk per core at a time: each build holds its chunk in memory, so the peak is
        // the chunk size times the degree of parallelism, not the size of the cloud.
        var options2 = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.For(0, plan.ChunkCount, options2, chunkIndex =>
        {
            if (chunkCounts[chunkIndex] == 0)
                return;

            string path = Path.Combine(scratchDirectory, $"chunk{chunkIndex:D5}.bin");
            StorePoint[] points = ChunkWriter.ReadAll(path, (int)chunkCounts[chunkIndex]);
            (float min, float max)[] cube = plan.ChunkCube(chunkIndex, bounds);

            var cells = new List<PointTileCell>();
            PointTileCellBuilder.Build(points, points.Length, cube, plan.ChunkLevel, options, cells);
            writer.Append(cells);

            long finished = Interlocked.Add(ref done, chunkCounts[chunkIndex]);
            progress?.Report(new PointTileBuildProgress(PointTileBuildStage.BuildingCells, finished, total));
        });

        return done;
    }
}

/// <summary>What a completed build produced, and where the time went.</summary>
public readonly record struct PointTileBuildReport(
    long SourcePoints,
    long StoredPoints,
    int NodeCount,
    int MaxLevel,
    int ChunkCount,
    TimeSpan Survey,
    TimeSpan Distribute,
    TimeSpan BuildCells,
    TimeSpan Write,
    TimeSpan Total);
