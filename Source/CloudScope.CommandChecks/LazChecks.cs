using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using CloudScope;
using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Library;
using CloudScope.Loading;
using CloudScope.Store;
using CloudScope.Ui;

internal static class LazChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-laz-őrült próbák-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            int[] strides = [20, 28, 26, 34, 57, 63, 30, 36, 38, 59, 67];
            for (byte format = 0; format <= 10; format++)
            {
                string source = Path.Combine(directory, $"source-{format}.las"), compressed = Path.Combine(directory, $"encoded-{format}.laz");
                File.WriteAllBytes(source, Fixture(format, strides[format]));
                LazNative.Transcode(source, compressed, 3, compressed: true);
                using var las = new LasReader(source);
                using var laz = new LasReader(compressed);
                var expected = las.GetPoints().ToArray();
                var actual = laz.GetPoints().ToArray();
                check($"LAZ format {format} preserves all fields present in its LAS record",
                    laz.IsCompressed && laz.Header.PointDataFormatId == format && laz.PointCount == 3 && actual.SequenceEqual(expected), "");
                var batch = new LasPoint[3]; laz.ReadBatch(2, batch, 1);
                bool random = batch[0].Equals(expected[2]); laz.ReadBatch(0, batch, 3);
                check($"LAZ format {format} supports forward and backward seeking with reusable batches", random && batch.SequenceEqual(expected), "");
                Array.Clear(batch); long count = laz.FillBuffer(batch, 2);
                check($"LAZ format {format} FillBuffer honors the requested prefix", count == 2 && batch.Take(2).SequenceEqual(expected.Take(2)) && batch[2].Equals(default(LasPoint)), "");
                var a = PointCloudLoader.Load(las, 0).ToDataset(); var b = PointCloudLoader.Load(laz, 0).ToDataset();
                check($"LAZ format {format} enters the common source-index/attribute pipeline losslessly", a.SourceIdentity == b.SourceIdentity
                    && a.SourcePayloadSha256 == b.SourcePayloadSha256, "");
                using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
                try { PointCloudLoader.Load(laz, 0, cancellation: cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
                check($"LAZ format {format} retains the common import cancellation contract", cancelled, "");
            }
            string lazPath = Path.Combine(directory, "encoded-7.laz");
            using (var viewer = new ViewerController(640, 480, new HeadlessBackend()))
            {
                viewer.Load(); var commands = new ViewerCommandDispatcher(viewer);
                var opened = commands.Execute($"OPEN \"{lazPath}\" 0"); Pump(viewer, () => viewer.LoadProgress < 0);
                check("OPEN decodes LAZ through the same background lifecycle as LAS", opened.Status == CommandStatus.Ended && viewer.Status.LoadedCount == 3
                    && viewer.CaptureProject().Source!.Format == "LAZ", opened.Message);
                string labels = Path.Combine(directory, "labels.json");
                File.WriteAllText(labels, "{\"labels\":{\"Tree\":[1]}}"); commands.Execute($"LOADLABELS Transfer \"{labels}\"");
                string project = Path.Combine(directory, "laz-project.cloudscope"); viewer.SaveProject(project);
                using var restored = new ViewerController(640, 480, new HeadlessBackend()); restored.Load(); restored.OpenProject(project);
                Pump(restored, () => restored.LoadProgress < 0);
                check("LAZ source projects reopen with verified identity and annotations", restored.CaptureProject().Source!.Format == "LAZ" && restored.LabelledPointCount == 1 && !restored.HasUnsavedProject, "");
                string json = Path.Combine(directory, "laz-labels.json");
                check("LAZ annotations can be saved as JSON without a temporary LAS", restored.SaveLabels(json) && File.Exists(json), "");
            }
            string storePath = Path.Combine(directory, "laz-store");
            var report = PointTileStoreBuilder.Build(lazPath, storePath, PointTileBuildOptions.Default with { WriteSourceIndices = true });
            using (var store = PointTileStore.Open(storePath))
            {
                var indices = new long[3];
                check("INDEX reads LAZ directly and retains source record numbers", report.SourcePoints == 3 && report.StoredPoints == 3
                    && store.ReadSourceIndices(0, indices) && indices.Order().SequenceEqual(new long[] { 0, 1, 2 }), "");
            }
            string destination = Path.Combine(directory, "protected.las"); File.WriteAllText(destination, "original"); bool rejected = false;
            try { LasClassificationWriter.Write(lazPath, destination, new Dictionary<int, byte> { [0] = 2 }); } catch (InvalidOperationException) { rejected = true; }
            check("Raw classification patching cannot corrupt compressed point bytes", rejected && File.ReadAllText(destination) == "original", "");
            byte[] broken = File.ReadAllBytes(lazPath); int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(broken.AsSpan(96));
            string bad = Path.Combine(directory, "truncated.laz"); File.WriteAllBytes(bad, broken.AsSpan(0, offset + 12).ToArray());
            bool truncated = false;
            try { using var reader = new LasReader(bad); reader.GetPoints().ToArray(); } catch (InvalidDataException) { truncated = true; }
            check("Truncated LAZ data fails before publishing a partial cloud", truncated, "");
            string wide = Path.Combine(directory, "wide-records.las"); File.WriteAllBytes(wide, Fixture(3, ushort.MaxValue));
            using (var reader = new LasReader(wide))
            {
                check("Large extra-byte records use a bounded raw scratch buffer", reader.PreferredBatchPoints(1 << 18) *
                    (long)reader.Header.PointDataRecordLength <= 32L * 1024 * 1024 && reader.GetPoints().Count() == 3, "");
            }
            var wideReport = PointTileStoreBuilder.Build(wide, Path.Combine(directory, "wide-store"));
            check("Indexing maximum-length LAS records does not overflow its raw buffer size", wideReport.StoredPoints == 3, "");
            using var archive = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "laszip-source.tar.gz"));
            using var gzip = new GZipStream(archive, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                string name = Path.GetFileName(entry.Name);
                if (!name.StartsWith("broken_", StringComparison.Ordinal) || !name.EndsWith(".laz", StringComparison.Ordinal) || entry.DataStream == null) continue;
                string path = Path.Combine(directory, name);
                using (var output = File.Create(path)) entry.DataStream.CopyTo(output);
                bool invalid = false;
                try { using var reader = new LasReader(path); reader.GetPoints().ToArray(); } catch (InvalidDataException) { invalid = true; }
                check($"Upstream malformed LAZ fixture {name} fails through the normal error channel", invalid, "");
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Fixture(byte format, int stride)
    {
        byte minor = format >= 6 ? (byte)4 : format >= 4 ? (byte)3 : (byte)2;
        int header = minor >= 4 ? 375 : minor == 3 ? 235 : 227;
        var one = LasChecks.Fixture(format, minor, stride);
        var data = new byte[header + 3 * stride]; one.AsSpan(0, header).CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(107), 3); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(111), 3);
        if (minor >= 4) BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(247), 3);
        for (int axis = 0; axis < 3; axis++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(179 + 16 * axis), (axis + 1) * 1001 + .02);
            BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(187 + 16 * axis), (axis + 1) * 1001);
        }
        for (int i = 0; i < 3; i++)
        {
            var p = data.AsSpan(header + i * stride, stride); one.AsSpan(header, stride).CopyTo(p);
            for (int axis = 0; axis < 3; axis++) BinaryPrimitives.WriteInt32LittleEndian(p[(axis * 4)..], (axis + 1) * 100 + i);
            BinaryPrimitives.WriteUInt16LittleEndian(p[12..], 45678);
            p[14] = format >= 6 ? (byte)0xed : (byte)0xc9;
            p[15] = format >= 6 ? (byte)0xff : (byte)0xe2;
            if (format >= 6)
            { p[16] = 200; p[17] = 89; BinaryPrimitives.WriteInt16LittleEndian(p[18..], -1200); BinaryPrimitives.WriteUInt16LittleEndian(p[20..], 65000); }
            else { p[16] = unchecked((byte)-37); p[17] = 89; BinaryPrimitives.WriteUInt16LittleEndian(p[18..], 65000); }
            int gps = format >= 6 ? 22 : format is 1 or 3 or 4 or 5 ? 20 : -1;
            if (gps >= 0) BinaryPrimitives.WriteDoubleLittleEndian(p[gps..], 123456.125);
            int rgb = format >= 6 ? (format is 7 or 8 or 10 ? 30 : -1) : format == 2 ? 20 : format is 3 or 5 ? 28 : -1;
            if (rgb >= 0) { BinaryPrimitives.WriteUInt16LittleEndian(p[rgb..], 1000); BinaryPrimitives.WriteUInt16LittleEndian(p[(rgb + 2)..], 32768); BinaryPrimitives.WriteUInt16LittleEndian(p[(rgb + 4)..], 65535); }
            if (format is 8 or 10) BinaryPrimitives.WriteUInt16LittleEndian(p[36..], 33000);
            int wave = format switch { 4 => 28, 5 => 34, 9 => 30, 10 => 38, _ => -1 };
            if (wave >= 0)
            {
                var w = p[wave..]; w[0] = 1; BinaryPrimitives.WriteUInt64LittleEndian(w[1..], 12345678); BinaryPrimitives.WriteUInt32LittleEndian(w[9..], 2048);
                BinaryPrimitives.WriteSingleLittleEndian(w[13..], .25f); BinaryPrimitives.WriteSingleLittleEndian(w[17..], 1.25f);
                BinaryPrimitives.WriteSingleLittleEndian(w[21..], -2.5f); BinaryPrimitives.WriteSingleLittleEndian(w[25..], .125f);
            }
        }
        return data;
    }
    private static void Pump(ViewerController viewer, Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do { viewer.UpdateFrame(1f / 60, new ViewerKeyboardState()); if (done()) return; Thread.Sleep(2); }
        while (timer.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException("LAZ workflow did not complete.");
    }
}
