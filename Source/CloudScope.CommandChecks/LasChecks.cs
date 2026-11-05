using System.Buffers.Binary;
using CloudScope.Library;
using CloudScope.Labeling;
using CloudScope.Loading;

internal static class LasChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-las-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "sample.las");
        try
        {
            int[] lengths = [20, 28, 26, 34, 57, 63, 30, 36, 38, 59, 67];
            for (byte format = 0; format <= 10; format++)
            {
                byte version = format >= 6 ? (byte)4 : format >= 4 ? (byte)3 : (byte)2;
                byte[] bytes = Fixture(format, version, lengths[format]);
                File.WriteAllBytes(file, bytes);
                using (var reader = new LasReader(file))
                {
                    LasPoint point = reader.GetPoints().Single();
                    var batch = new LasPoint[1];
                    reader.ReadBatch(0, batch, 1);
                    check($"LAS format {format}: short/versioned header, coordinates, class and batch parity",
                        reader.PointCount == 1 && point.X == 1001 && point.Y == 2002 && point.Z == 3003 &&
                        (byte)point.Classification == 2 && batch[0].Z == point.Z && point.ReturnNumber == 1, "");
                    var cloud = PointCloudLoader.Load(reader, 0);
                    check($"LAS format {format}: resident import", cloud.LoadedCount == 1 && cloud.Points[0].X == 0, "");
                }
                string labeled = Path.Combine(directory, "labeled.las");
                LasClassificationWriter.Write(file, labeled, new Dictionary<int, byte> { [0] = 6 });
                using var updated = new LasReader(labeled);
                check($"LAS format {format}: classification export preserves flags", (byte)updated.GetPoints().Single().Classification == 6 &&
                    updated.GetPoints().Single().ClassificationSynthetic, "");
            }
            foreach ((string name, Action<byte[]> mutate) in new (string, Action<byte[]>)[]
            {
                ("signature", bytes => bytes[0] = 0),
                ("compressed LAZ", bytes => bytes[104] |= 128),
                ("record stride", bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(105), 1)),
                ("point data offset", bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(96), 1)),
                ("point count", bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(107), 100)),
                ("non-finite scale", bytes => WriteDouble(bytes, 131, double.NaN)),
                ("reversed bounds", bytes => WriteDouble(bytes, 187, 9999))
            })
            {
                byte[] bytes = Fixture(0, 2, 20); mutate(bytes); File.WriteAllBytes(file, bytes);
                bool rejected = false;
                try { using var reader = new LasReader(file); } catch (InvalidDataException) { rejected = true; }
                check($"LAS rejects invalid {name} before parsing points", rejected, "");
            }
            File.WriteAllBytes(file, Fixture(0, 2, 20));
            using (var reader = new LasReader(file))
            using (var cancellation = new CancellationTokenSource())
            {
                bool preCancelled = false, midCancelled = false;
                try { PointCloudLoader.Load(reader, 0, cancellation: new CancellationToken(true)); }
                catch (OperationCanceledException) { preCancelled = true; }
                try { PointCloudLoader.Load(reader, 0, new CancellationProgress(_ => cancellation.Cancel()), cancellation.Token); }
                catch (OperationCanceledException) { midCancelled = true; }
                check("LAS honours pre-cancellation and cancellation during import", preCancelled && midCancelled, "");
            }
            string existingOutput = Path.Combine(directory, "existing.las");
            File.WriteAllText(existingOutput, "original destination");
            bool classRejected = false;
            try { LasClassificationWriter.Write(file, existingOutput, new Dictionary<int, byte> { [0] = 200 }); }
            catch (InvalidDataException) { classRejected = true; }
            check("Legacy LAS rejects unrepresentable class codes without truncating output", classRejected && File.ReadAllText(existingOutput) == "original destination", "");
            byte[] original = File.ReadAllBytes(file);
            bool sourceProtected = false;
            try { LasClassificationWriter.Write(file, file, new Dictionary<int, byte> { [0] = 6 }); }
            catch (ArgumentException) { sourceProtected = true; }
            check("LAS classification export protects its source", sourceProtected && File.ReadAllBytes(file).SequenceEqual(original), "");
            File.WriteAllBytes(file, new byte[100]);
            bool truncated = false;
            try { using var reader = new LasReader(file); } catch (InvalidDataException) { truncated = true; }
            check("LAS rejects a truncated header", truncated, "");
            byte[] vlr = Fixture(0, 2, 20);
            BinaryPrimitives.WriteUInt32LittleEndian(vlr.AsSpan(100), 1);
            File.WriteAllBytes(file, vlr);
            bool invalidVlr = false;
            try { using var reader = new LasReader(file); reader.VlrManager.GetVariableLengthRecords().ToArray(); }
            catch (InvalidDataException) { invalidVlr = true; }
            check("LAS rejects VLRs overlapping point records", invalidVlr, "");
        }
        finally { Directory.Delete(directory, true); }
    }
    internal static byte[] Fixture(byte format, byte minor, int stride)
    {
        int header = minor >= 4 ? 375 : minor == 3 ? 235 : 227;
        var bytes = new byte[header + stride];
        "LASF"u8.CopyTo(bytes);
        bytes[24] = 1; bytes[25] = minor;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(94), (ushort)header);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(96), (uint)header);
        bytes[104] = format;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(105), (ushort)stride);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(107), 1);
        if (minor >= 4) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(247), 1);
        for (int i = 0; i < 3; i++)
        {
            WriteDouble(bytes, 131 + i * 8, .01);
            WriteDouble(bytes, 155 + i * 8, (i + 1) * 1000);
            WriteDouble(bytes, 179 + i * 16, (i + 1) * 1001);
            WriteDouble(bytes, 187 + i * 16, (i + 1) * 1001);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + i * 4), (i + 1) * 100);
        }
        bytes[header + 14] = format >= 6 ? (byte)0x11 : (byte)9;
        bytes[header + 15] = format >= 6 ? (byte)1 : (byte)0x22;
        if (format >= 6) bytes[header + 16] = 2;
        return bytes;
    }
    private static void WriteDouble(byte[] bytes, int offset, double value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));
}
