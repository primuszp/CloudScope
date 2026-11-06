using System.Diagnostics;
using System.Text.Json;
using CloudScope;
using CloudScope.Forestry;
using CloudScope.Library;
using CloudScope.Terrain;

internal static class GroundReferenceBenchmark
{
    public static void Run(string groundPath, string otherPath, string output)
    {
        Directory.CreateDirectory(output);
        using var ground = new LasReader(groundPath); using var other = new LasReader(otherPath);
        double cx = (Math.Max(ground.Header.MinX, other.Header.MinX) + Math.Min(ground.Header.MaxX, other.Header.MaxX)) * .5;
        double cy = (Math.Max(ground.Header.MinY, other.Header.MinY) + Math.Min(ground.Header.MaxY, other.Header.MaxY)) * .5;
        double cz = ground.Header.MinZ;
        var points = new List<PointData>(); var reference = new List<bool>();
        var watch = Stopwatch.StartNew();
        int Read(LasReader reader, bool isGround)
        {
            int start = points.Count;
            var batch = new LasPoint[65536];
            long stride = Math.Max(1, reader.PointCount / 1_000_000);
            for (long first = 0; first < reader.PointCount;)
            {
                int count = reader.ReadBatch(first, batch, batch.Length);
                if (count == 0) throw new InvalidDataException("Unexpected end of reference survey.");
                for (int i = 0; i < count; i++)
                {
                    if ((first + i) % stride != 0) continue;
                    var p = batch[i];
                    if (Math.Abs(p.X - cx) > 50 || Math.Abs(p.Y - cy) > 50) continue;
                    points.Add(new() { X = (float)(p.X - cx), Y = (float)(p.Y - cy), Z = (float)(p.Z - cz) }); reference.Add(isGround);
                }
                first += count;
            }
            return points.Count - start;
        }
        int groundCount = Read(ground, true), otherCount = Read(other, false);
        double readSeconds = watch.Elapsed.TotalSeconds;
        Console.WriteLine($"Reference crop: {groundCount} ground, {otherCount} other points, read {readSeconds:0.0}s.");
        var cases = new List<object>();
        foreach (float resolution in new[] { .5f, 1f, 2f })
        {
            var options = new GroundSegmentationOptions { ClothResolution = resolution };
            watch.Restart(); var result = GroundSegmentation.Segment(points, options);
            var metrics = ClassificationMetrics.Evaluate(reference, result.PointIndices);
            cases.Add(new { Options = options, Seconds = watch.Elapsed.TotalSeconds, Metrics = metrics, result.FailureReason });
            Console.WriteLine($"CSF {resolution}: precision={metrics.Precision:0.0000}, recall={metrics.Recall:0.0000}, F1={metrics.F1:0.0000}.");
        }
        watch.Restart();
        var terrain = TerrainTriangulation.Build(points, new(.5, 2), (cx, cy, cz), include: i => reference[i]);
        var report = new
        {
            GroundSource = Path.GetFullPath(groundPath),
            OtherSource = Path.GetFullPath(otherPath),
            ReferenceMeaning = "Agreement with the supplied ground/off-ground file split; not independently verified field truth.",
            CenterX = cx,
            CenterY = cy,
            HalfWidth = 50,
            GroundCount = groundCount,
            OtherCount = otherCount,
            ReadSeconds = readSeconds,
            Sampling = "Every floor(filePointCount/1,000,000)th source record inside a common 100x100 coordinate-unit crop.",
            Cases = cases,
            Terrain = new { Sites = terrain.Vertices.Length, Triangles = terrain.Indices.Length / 3, Seconds = watch.Elapsed.TotalSeconds }
        };
        File.WriteAllText(Path.Combine(output, "ground-reference.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
