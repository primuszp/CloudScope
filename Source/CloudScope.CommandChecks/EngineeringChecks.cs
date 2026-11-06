using System.IO.Compression;
using System.Text.Json.Nodes;
using CloudScope;
using CloudScope.Commands;
using CloudScope.Measurements;
using CloudScope.Projects;
using CloudScope.Sections;
using CloudScope.Ui;
using OpenTK.Mathematics;

internal static class EngineeringChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        var distance = new Measurement(1, "D", MeasurementKind.Distance, [Vector3.Zero, new(3, 4, 12)]).Evaluate();
        check("Engineering distance uses 3D, horizontal, signed height and percent slope", distance.Spatial == 13 && distance.Horizontal == 5 && distance.DeltaZ == 12 && distance.SlopePercent == 240, "");
        var vertical = new Measurement(1, "V", MeasurementKind.Distance, [new(1, 2, 3), new(1, 2, -2)]).Evaluate();
        check("Vertical downward measurement has no finite percent slope", vertical.Spatial == 5 && vertical.DeltaZ == -5 && vertical.SlopePercent == null && vertical.AngleDegrees == -90, "");
        Vector3[] boundary = [new(1000000, 2000000, 0), new(1000004, 2000000, 2), new(1000004, 2000003, 3), new(1000000, 2000003, 1)];
        var area = new Measurement(1, "A", MeasurementKind.Area, boundary).Evaluate();
        check("Translated rectangle has 12 square units and 14 plan perimeter", area.Area == 12 && area.Horizontal == 14, "");
        check("Area is independent of boundary winding", new Measurement(1, "A", MeasurementKind.Area, boundary.Reverse().ToArray()).Evaluate().Area == 12, "");
        Vector3[] concave = [new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(2, 2, 0), new(0, 4, 0)];
        check("Concave boundary retains its cutout", new Measurement(1, "A", MeasurementKind.Area, concave).Evaluate().Area == 12, "");
        bool Invalid(Vector3[] points)
        { try { new Measurement(1, "A", MeasurementKind.Area, points).Evaluate(); return false; } catch (InvalidDataException) { return true; } }
        check("Self-intersecting and self-touching area boundaries are rejected", Invalid([new(0, 0, 0), new(2, 2, 0), new(0, 2, 0), new(2, 0, 0)])
            && Invalid([new(0, 0, 0), new(2, 0, 0), new(1, 0, 0), new(0, 2, 0)]), "");
        check("Degenerate and nonfinite measurements are rejected", Invalid([Vector3.Zero, Vector3.UnitX, new(2, 0, 0)]) && Invalid([Vector3.Zero, Vector3.One, new(float.NaN, 0, 0)]), "");

        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-engineering-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var viewer = new ViewerController(1280, 720, new HeadlessBackend()); viewer.Load();
            viewer.LoadPointCloud([new() { X = 0, Z = 1 }, new() { X = 1, Z = 2 }, new() { X = 2, Z = 4 }, new() { X = 1, Y = 2, Z = 9 }], 10);
            var commands = new ViewerCommandDispatcher(viewer);
            var result = commands.Execute("MEASURE Distance \"Survey D\" 0,0,0 3,4,12");
            check("MEASURE completes from scripted points and quoted names", result.Status == CommandStatus.Ended && viewer.Measurements.Count == 1 && result.Message.Contains("13"), result.Message);
            commands.Execute("MEASURE Area A 0,0,0 4,0,0 4,3,0 0,3,0 Close");
            check("MEASURE Area commits one closed boundary", viewer.Measurements.Count == 2 && viewer.Measurements[1].Evaluate().Area == 12, "");
            commands.Execute("UNDO 1"); check("Measurement is a single undo transaction", viewer.Measurements.Count == 1, "");
            commands.Execute("REDO 1");
            viewer.CreateCrossSection(new(0, 0, 0), new(2, 0, 0), 1); viewer.RenameCrossSection("Longitudinal");
            viewer.CreateCrossSection(new(0, 0, 0), new(0, 3, 0), 2); viewer.RenameCrossSection("Transverse");
            check("Creating a second section preserves the named first section", viewer.Sections.Count == 2 && viewer.Sections[0].Name == "Longitudinal", "");
            commands.Execute("XSECTION Activate Longitudinal");
            check("Named section activation restores its stable identity", viewer.CaptureProject().ActiveSectionId == viewer.Sections[0].Id, "");
            viewer.ClearCrossSection(); commands.Execute("UNDO 1");
            check("Section removal undo restores the collection and active section", viewer.Sections.Count == 2 && viewer.CaptureProject().ActiveSectionId == viewer.Sections[0].Id, "");
            string path = Path.Combine(directory, "work.cloudscope"); viewer.SaveProject(path);
            var saved = viewer.CaptureProject();
            using var reopened = new ViewerController(1280, 720, new HeadlessBackend()); reopened.Load();
            reopened.OpenProject(path); Pump(reopened, () => reopened.LoadProgress < 0);
            check("Project v2 restores measurements, every section and active identity", ProjectFile.Serialize(reopened.CaptureProject()) == ProjectFile.Serialize(saved), "");
            viewer.AddMeasurement("Unsaved", MeasurementKind.Distance, [Vector3.Zero, Vector3.One]);
            check("Measurement content participates in project save protection", viewer.HasUnsavedProject, "");
            commands.Execute("UNDO 1"); check("Measurement undo returns to the saved checkpoint", !viewer.HasUnsavedProject, "");
            string csv = Path.Combine(directory, "measurements.csv"), dxf = Path.Combine(directory, "measurements.dxf");
            commands.Execute($"MEASURE Export Csv \"{csv}\""); commands.Execute($"MEASURE Export Dxf \"{dxf}\"");
            check("Measurement CSV contains the independent 3-4-12 result", File.ReadAllLines(csv).Any(l => l.Contains(",13,5,12,240,")), "");
            string[] groups = File.ReadAllLines(dxf);
            check("Measurement DXF contains complete R12 3D polyline entities", groups.Contains("AC1009") && groups.Count(l => l == "POLYLINE") == 2 && groups.Count(l => l == "VERTEX") == 6 && groups[^1] == "EOF", "");
            string sectionCsv = Path.Combine(directory, "section.csv");
            commands.Execute("THIN 10"); commands.Execute($"XSECTION Export Csv \"{sectionCsv}\"");
            Pump(viewer, () => viewer.ProcessingProgress < 0);
            var rows = File.ReadAllLines(sectionCsv);
            check("Section CSV exports all three slab points independent of display thinning", rows.Length == 4 && rows.Any(l => l.Contains(",-1,2,2,4,0,2,0,4")), string.Join("|", rows));
            string profileDxf = Path.Combine(directory, "section.dxf");
            commands.Execute($"XSECTION Export Dxf \"{profileDxf}\""); Pump(viewer, () => viewer.ProcessingProgress < 0);
            check("Section DXF exports station/elevation points", File.ReadAllLines(profileDxf).Count(l => l == "POINT") == 3, "");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool stopped = false; File.WriteAllText(csv, "original");
            try { EngineeringExport.Section(csv, EngineeringExportFormat.Csv, viewer.Sections[0], [], cancelled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            check("Cancelled engineering export preserves the prior file", stopped && File.ReadAllText(csv) == "original", "");

            string legacy = Path.Combine(directory, "v1.cloudscope");
            using (var source = ZipFile.OpenRead(path))
            using (var target = ZipFile.Open(legacy, ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries)
                {
                    using var output = target.CreateEntry(entry.FullName).Open();
                    using var input = entry.Open();
                    if (entry.FullName != "project.json") { input.CopyTo(output); continue; }
                    var node = JsonNode.Parse(input)!; node["Version"] = 1;
                    node["Section"] = node["Sections"]![0]!.DeepClone();
                    foreach (string key in new[] { "Sections", "ActiveSectionId", "Measurements" }) node.AsObject().Remove(key);
                    using var writer = new StreamWriter(output); writer.Write(node.ToJsonString());
                }
            }
            var upgraded = ProjectFile.Read(legacy).State;
            check("Legacy v1 project migrates its single section without losing source data", upgraded.Version == 3 && upgraded.Sections.Length == 1 && upgraded.Measurements.Length == 0 && upgraded.Source == saved.Source, "");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Pump(ViewerController viewer, Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        do { viewer.UpdateFrame(1f / 60, new ViewerKeyboardState()); if (done()) return; Thread.Sleep(2); }
        while (watch.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("Engineering operation timed out.");
    }
}
