using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json.Nodes;
using CloudScope;
using CloudScope.Drawing;
using CloudScope.Labeling;
using CloudScope.Loading;
using CloudScope.Measurements;
using CloudScope.Projects;
using CloudScope.Rendering;
using CloudScope.Selection;
using CloudScope.Store;
using CloudScope.Terrain;
using OpenTK.Mathematics;

internal static class ConsolidationChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        foreach (int count in new[] { 0, 1 })
        {
            var data = new PointCloudDataset([new() { X = 1 }, new() { X = 999 }], count, 1, false, 1,
                new(new byte[2], new ushort[2], new byte[2], new double[2], 0, 0));
            data.ApplyFilter(null, recolor: false);
            check($"A {count}-point view never exposes spare source-buffer entries", data.ViewPoints.Length == count && data.SourcePoints.Length == count
                && new PointCloudRenderData(data.ViewPoints, data.RenderOrder).Count == count, "");
        }
        var editor = new SceneEditor(); var line = new Polyline3D(1, "Line", [Vector3.Zero, Vector3.One]);
        var target = editor.Add(line); editor.RecordCreation(line);
        var edited = line with { Vertices = [Vector3.Zero, new(4, 5, 6)] };
        target.RestoreState(edited); editor.RecordEdit(target.EditKey, "Edit", line, edited);
        editor.History.Undo(2); editor.History.Redo(2);
        check("Scene edit redo addresses the recreated entity by stable identity", editor.Polylines.Single().Polyline.Vertices[1] == new Vector3(4, 5, 6)
            && !ReferenceEquals(target, editor.Polylines[0]), "");
        check("Scene owners have independent entities and histories", new SceneEditor().Polylines.Count == 0 && new SceneEditor().History.Undo() == 0, "");

        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-consolidation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string las = Path.Combine(directory, "a.las"), secondLas = Path.Combine(directory, "b.las");
            var bytes = LasChecks.Fixture(0, 2, 20); File.WriteAllBytes(las, bytes);
            foreach (int offset in new[] { 155, 179, 187 })
                BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(offset), BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset)) + 10);
            File.WriteAllBytes(secondLas, bytes);
            string first = Path.Combine(directory, "first"), second = Path.Combine(directory, "second");
            PointTileStoreBuilder.Build(las, first); PointTileStoreBuilder.Build(secondLas, second);
            using var viewer = NewViewer(); viewer.OpenPointTileStore(first); viewer.OpenPointTileStore(second, false);
            var frame = viewer.CaptureProject().Frame;
            check("Small multi-layer stores allocate one physical page per occupied cell", PointTileWorkingSet.Pages(viewer.Layers, 2 * 65536, 65536) == 2
                && PointTileWorkingSet.Pages(viewer.Layers, 65536, 65536) == 1, "");
            check("Distinct store origins share one stable document coordinate frame", viewer.Layers[0].Offset == Vector3.Zero && viewer.Layers[1].Offset == new Vector3(10, 0, 0), "");
            var raw = new GpuPointVertex[1]; viewer.Layers[1].Store.ReadPoints(0, raw);
            var expected = new Vector3(raw[0].X + 10, raw[0].Y, raw[0].Z);
            var source = new StreamedSelectionSource(viewer.Layers);
            source.ResolveAndRemember(new Nearby(expected, .1f));
            check("Layer-aware selection catches the world-positioned second survey only", source.LabelRefs.Count > 0 && source.LabelRefs.All(r => r.LayerIndex == 1)
                && source.LabelPoints[0].X == expected.X, "");
            var local = viewer.Layers[1].LocalView(new(Matrix4.Identity, Matrix4.Identity, 800, 600, 1));
            check("GPU layer view applies the same document offset as selection", new Vector4(raw[0].X, raw[0].Y, raw[0].Z, 1) * local.View == new Vector4(expected, 1), "");
            var camera = new OrbitCamera(); camera.SetViewportSize(800, 600); camera.SetOrthographicStandardView("Top", 1);
            var visits = new PointTileVisit[10];
            int visible = PointTileTraversal.Collect(viewer.Layers, new(camera.GetViewMatrix(), camera.GetProjectionMatrix(), 800, 600, 1), 100, visits, new(), out _);
            check("Traversal frustum uses document-space layer positions", visible > 0 && visits.Take(visible).All(v => v.LayerIndex == 0), "");
            PointTileLabelFile.Save(first, [new PointRef(1, 0)], new Dictionary<int, PointAnnotation> { [0] = new("Tree", 4) }); viewer.LoadLabels();
            viewer.CreateCrossSection(new(8, -2, 0), new(12, 2, 0), 5);
            viewer.AddMeasurement("Offset", MeasurementKind.Distance, [Vector3.Zero, expected]);
            string path = Path.Combine(directory, "layers.cloudscope"); viewer.SaveProject(path);
            string legacy = Path.Combine(directory, "v2.cloudscope");
            using (var original = ZipFile.OpenRead(path))
            using (var old = ZipFile.Open(legacy, ZipArchiveMode.Create))
            using (var input = original.GetEntry("project.json")!.Open())
            using (var output = new StreamWriter(old.CreateEntry("project.json").Open()))
            {
                var json = JsonNode.Parse(input)!; json["Version"] = 2; json.AsObject().Remove("Frame");
                // Recreate the actual legacy representation: label positions were store-local.
                foreach (var annotation in json["Annotations"]!.AsArray())
                {
                    int layer = annotation!["Layer"]!.GetValue<int>();
                    var offset = viewer.Layers[layer].Offset;
                    var position = annotation["Position"]!;
                    annotation["Position"] = new JsonArray(position[0]!.GetValue<float>() - offset.X,
                        position[1]!.GetValue<float>() - offset.Y, position[2]!.GetValue<float>() - offset.Z);
                }
                output.Write(json.ToJsonString());
            }
            var migrated = ProjectFile.Read(legacy).State;
            check("Legacy v2 streamed projects migrate the frame and store-local sparse label positions", migrated.Frame == frame
                && migrated.Annotations.Single().Position == expected, "");
            using (var restored = NewViewer())
            {
                restored.OpenProject(path); Pump(restored, () => restored.LoadProgress < 0);
                check("Project restores layer offsets and document-positioned sparse labels", restored.Layers.Count == 2 && restored.Layers[1].Offset.X == 10
                    && restored.CaptureProject().Annotations is [{ Layer: 1, Instance: 4, Position: var p }] && p == expected, "");
            }
            viewer.CloseLayer("first"); viewer.SaveProject(path);
            using (var restored = NewViewer())
            {
                restored.OpenProject(path); Pump(restored, () => restored.LoadProgress < 0);
                check("Closing the first layer preserves the document frame, measurements and remaining annotations", restored.CaptureProject().Frame == frame
                    && restored.Layers.Count == 1 && restored.Layers[0].Offset.X == 10 && restored.Measurements.Count == 1
                    && restored.CaptureProject().Annotations is [{ Layer: 0, Position: var p }] && p == expected, "");
                string csv = Path.Combine(directory, "section.csv"); restored.ExportCrossSection(csv, EngineeringExportFormat.Csv);
                Pump(restored, () => restored.ProcessingProgress < 0);
                check("Section export resolves a shifted layer in document coordinates", File.ReadAllLines(csv).Length == 2, "");
            }

            var plane = (from y in Enumerable.Range(0, 4) from x in Enumerable.Range(0, 4) select new PointData { X = x, Y = y, Z = x + y }).ToArray();
            using (var terrain = NewViewer())
            {
                terrain.LoadPointCloud(plane, 10); terrain.BuildTerrain(false, new(2, 5)); Pump(terrain, () => terrain.Status.SurfaceProgress < 0);
                string project = Path.Combine(directory, "terrain.cloudscope"); terrain.SaveProject(project);
                using var restored = NewViewer(); restored.OpenProject(project); Pump(restored, () => restored.LoadProgress < 0);
                check("A terrain mesh with fewer/reordered sites survives full project restore", restored.Status.SurfaceTriangles > 0 && restored.Status.SurfaceTriangles == terrain.Status.SurfaceTriangles, "");
            }
            using (var store = PointTileStore.Open(first)) { }
            byte[] validIndex = File.ReadAllBytes(Path.Combine(first, PointTileStore.IndexFileName));
            bool Reject(byte[] index)
            {
                File.WriteAllBytes(Path.Combine(first, PointTileStore.IndexFileName), index);
                try { using var invalid = PointTileStore.Open(first); return false; } catch (InvalidDataException) { return true; }
            }
            var bad = validIndex.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(128 + 36), 0); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(128 + 40), 1);
            check("Store validation rejects self-referencing cells before traversal", Reject(bad), "");
            bad = validIndex.ToArray(); BinaryPrimitives.WriteInt64LittleEndian(bad.AsSpan(128 + 24), long.MaxValue);
            check("Store validation rejects invalid point ranges before mapping", Reject(bad), "");
            check("Store validation rejects truncated node tables", Reject(validIndex[..^1]), "");
            File.WriteAllBytes(Path.Combine(first, PointTileStore.IndexFileName), validIndex);
            string attributes = Path.Combine(first, PointTileStore.AttributeFileName); File.Delete(attributes);
            bool missing = false; try { using var invalid = PointTileStore.Open(first); } catch (InvalidDataException) { missing = true; }
            check("Declared but missing attribute columns cannot silently change a survey", missing, "");
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class Nearby(Vector3 center, float radius) : IPointSelectionQuery
    {
        public bool IsEmpty => false;
        public bool Contains(float x, float y, float z) => Vector3.DistanceSquared(center, new(x, y, z)) <= radius * radius;
        public void GetBounds(out Vector3 min, out Vector3 max) { min = center - new Vector3(radius); max = center + new Vector3(radius); }
        public IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default) => PointSelectionQuery.Resolve(this, points, cancellationToken);
    }
    private static ViewerController NewViewer() { var viewer = new ViewerController(1280, 720, new HeadlessBackend()); viewer.Load(); return viewer; }
    private static void Pump(ViewerController viewer, Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do { viewer.UpdateFrame(1f / 60, new ViewerKeyboardState()); if (done()) return; Thread.Sleep(2); } while (timer.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("Consolidation operation timed out.");
    }
}
