using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CloudScope.Loading;

/// <summary>Managed E57 reader for standard numeric bitpack streams, poses and spherical scans.</summary>
public static class E57PointCloudLoader
{
    public static LoadedPointCloud Load(string path, long maxPoints = 0, IProgress<int>? progress = null)
    {
        using var file = File.OpenRead(path);
        byte[] header = new byte[48]; file.ReadExactly(header);
        if (Encoding.ASCII.GetString(header, 0, 8) != "ASTM-E57" || U32(header, 8) != 1)
            throw new InvalidDataException("Not an E57 version 1 file.");
        if (U64(header, 16) != (ulong)file.Length) throw new InvalidDataException("E57 physical length mismatch.");
        int pageSize = checked((int)U64(header, 40));
        if (pageSize != 1024) throw new NotSupportedException("E57 page size must be 1024 bytes.");
        var paged = new Pages(file, pageSize);
        byte[] xml = paged.ReadPhysical(checked((long)U64(header, 24)), checked((int)U64(header, 32)));
        using var xmlReader = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var doc = XDocument.Load(xmlReader);
        XElement data = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "data3D")
            ?? throw new InvalidDataException("E57 contains no data3D.");
        var builder = new PointCloudBuilder();
        long limit = maxPoints > 0 ? maxPoints : int.MaxValue;
        int scanNumber = 0;
        foreach (var scan in data.Elements())
        {
            if (builder.Count >= limit) break;
            XElement? points = Child(scan, "points");
            if (points == null) continue;
            int count = checked((int)AttrLong(points, "recordCount"));
            if (count == 0) continue;
            if (Child(points, "codecs")?.Elements().Any() == true)
                throw new NotSupportedException("E57 explicit codecs are not supported; standard bitpack streams are required.");
            XElement[] fields = Child(points, "prototype")?.Elements().ToArray()
                ?? throw new InvalidDataException("E57 point prototype is missing.");
            long sectionPhysical = AttrLong(points, "fileOffset");
            byte[] sectionHeader = paged.ReadPhysical(sectionPhysical, 32);
            if (sectionHeader[0] != 1) throw new InvalidDataException("Invalid E57 compressed-vector section.");
            long logicalStart = paged.ToLogical(sectionPhysical);
            long end = checked(logicalStart + (long)U64(sectionHeader, 8));
            long cursor = paged.ToLogical(checked((long)U64(sectionHeader, 16)));
            var streams = fields.Select(_ => new MemoryStream()).ToArray();
            try
            {
                while (cursor < end)
                {
                    byte[] packetHeader = paged.ReadLogical(cursor, 4);
                    int packetLength = U16(packetHeader, 2) + 1;
                    if (packetLength < 4 || cursor + packetLength > end)
                        throw new InvalidDataException("Invalid E57 packet length.");
                    byte[] packet = paged.ReadLogical(cursor, packetLength);
                    cursor += packetLength;
                    if (packet[0] is 0 or 2) continue;
                    if (packet[0] != 1 || packetLength < 6) throw new InvalidDataException("Invalid E57 packet type.");
                    int streamCount = U16(packet, 4);
                    if (streamCount != fields.Length || 6 + 2 * streamCount > packetLength)
                        throw new InvalidDataException("E57 bytestream count mismatch.");
                    int offset = 6 + 2 * streamCount;
                    for (int f = 0; f < streamCount; f++)
                    {
                        int length = U16(packet, 6 + f * 2);
                        if (offset + length > packetLength) throw new InvalidDataException("Truncated E57 bytestream.");
                        streams[f].Write(packet, offset, length); offset += length;
                    }
                }
                int decodeCount = (int)Math.Min(count, limit - builder.Count);
                var values = new Dictionary<string, double[]>();
                for (int f = 0; f < fields.Length; f++)
                    values.Add(fields[f].Name.LocalName, Decode(fields[f], streams[f].ToArray(), decodeCount));
                bool cartesian = values.ContainsKey("cartesianX") && values.ContainsKey("cartesianY") && values.ContainsKey("cartesianZ");
                bool spherical = values.ContainsKey("sphericalRange") && values.ContainsKey("sphericalAzimuth") && values.ContainsKey("sphericalElevation");
                if (!cartesian && !spherical) throw new InvalidDataException("E57 scan has no supported coordinates.");
                XElement? pose = Child(scan, "pose"), rotation = Child(pose, "rotation"), translation = Child(pose, "translation");
                double qx = Value(rotation, "x", 0), qy = Value(rotation, "y", 0), qz = Value(rotation, "z", 0), qw = Value(rotation, "w", 1);
                double qlength = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
                if (!double.IsFinite(qlength) || qlength < 1e-20) throw new InvalidDataException("Invalid E57 rotation quaternion.");
                qx /= qlength; qy /= qlength; qz /= qlength; qw /= qlength;
                double tx = Value(translation, "x", 0), ty = Value(translation, "y", 0), tz = Value(translation, "z", 0);
                bool color = values.ContainsKey("colorRed") && values.ContainsKey("colorGreen") && values.ContainsKey("colorBlue");
                builder.HasColor |= color;
                double colorMax = color ? Math.Max(values["colorRed"].Max(), Math.Max(values["colorGreen"].Max(), values["colorBlue"].Max())) : 1;
                double colorScale = colorMax <= 1 ? 1 : colorMax <= 255 ? 255 : 65535;
                double intensityMax = Value(Child(scan, "intensityLimits"), "intensityMaximum", values.TryGetValue("intensity", out var ints) ? ints.Max() : 1);
                double intensityMin = Value(Child(scan, "intensityLimits"), "intensityMinimum", 0);
                for (int i = 0; i < decodeCount; i++)
                {
                    if (Get(cartesian ? "cartesianInvalidState" : "sphericalInvalidState", i) != 0) continue;
                    double x, y, z;
                    if (cartesian) { x = Get("cartesianX", i); y = Get("cartesianY", i); z = Get("cartesianZ", i); }
                    else
                    {
                        double r = Get("sphericalRange", i), az = Get("sphericalAzimuth", i), el = Get("sphericalElevation", i);
                        x = r * Math.Cos(el) * Math.Cos(az); y = r * Math.Cos(el) * Math.Sin(az); z = r * Math.Sin(el);
                    }
                    double vx = 2 * (qy * z - qz * y), vy = 2 * (qz * x - qx * z), vz = 2 * (qx * y - qy * x);
                    builder.Add(x + qw * vx + qy * vz - qz * vy + tx, y + qw * vy + qz * vx - qx * vz + ty,
                        z + qw * vz + qx * vy - qy * vx + tz,
                        color ? (float)(Get("colorRed", i) / colorScale) : 0.8f,
                        color ? (float)(Get("colorGreen", i) / colorScale) : 0.8f,
                        color ? (float)(Get("colorBlue", i) / colorScale) : 0.8f,
                        (ushort)Math.Clamp(Math.Round((Get("intensity", i) - intensityMin) / Math.Max(intensityMax - intensityMin, 1e-20) * 65535), 0, 65535));
                }
                double Get(string name, int i) => values.TryGetValue(name, out var array) ? array[i] : 0;
            }
            finally { foreach (var stream in streams) stream.Dispose(); }
            progress?.Report(++scanNumber * 100 / Math.Max(data.Elements().Count(), 1));
        }
        return builder.Build();
    }

    private static double[] Decode(XElement field, byte[] data, int count)
    {
        string type = (string?)field.Attribute("type") ?? "";
        bool floating = type == "Float";
        if (!floating && type is not ("Integer" or "ScaledInteger"))
            throw new NotSupportedException($"Unsupported E57 prototype field type: {type}");
        double min = AttrDouble(field, "minimum", 0), max = AttrDouble(field, "maximum", 0);
        int bits = floating ? ((string?)field.Attribute("precision") == "single" ? 32 : 64) :
            (max <= min ? 0 : BitOperations.Log2(checked((ulong)(max - min))) + 1);
        if ((long)bits * count > (long)data.Length * 8) throw new InvalidDataException("Truncated E57 numeric stream.");
        double scale = type == "ScaledInteger" ? AttrDouble(field, "scale", 1) : 1;
        double shift = type == "ScaledInteger" ? AttrDouble(field, "offset", 0) : 0;
        var values = new double[count];
        long position = 0;
        for (int i = 0; i < count; i++)
        {
            if (floating)
                values[i] = bits == 32 ? BitConverter.Int32BitsToSingle((int)U32(data, i * 4)) : BitConverter.Int64BitsToDouble((long)U64(data, i * 8));
            else
            {
                ulong raw = 0;
                for (int b = 0; b < bits; b++, position++)
                    raw |= (ulong)((data[position / 8] >> (int)(position % 8)) & 1) << b;
                values[i] = (raw + min) * scale + shift;
            }
        }
        return values;
    }
    private static XElement? Child(XElement? element, string name) => element?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
    private static double Value(XElement? element, string name, double fallback) => Child(element, name) is { } child
        ? (string.IsNullOrWhiteSpace(child.Value) ? 0 : double.Parse(child.Value, CultureInfo.InvariantCulture)) : fallback;
    private static double AttrDouble(XElement element, string name, double fallback) => element.Attribute(name) is { } a ? double.Parse(a.Value, CultureInfo.InvariantCulture) : fallback;
    private static long AttrLong(XElement element, string name) => long.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
    private static ushort U16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset));
    private static uint U32(byte[] b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset));
    private static ulong U64(byte[] b, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(offset));

    private sealed class Pages(FileStream file, int size)
    {
        private long _page = -1;
        private readonly byte[] _buffer = new byte[size];
        public long ToLogical(long physical) => checked(physical / size * (size - 4) + physical % size);
        public byte[] ReadPhysical(long physical, int count) => ReadLogical(ToLogical(physical), count);
        public byte[] ReadLogical(long logical, int count)
        {
            var result = new byte[count];
            int written = 0;
            while (written < count)
            {
                long page = logical / (size - 4);
                int offset = (int)(logical % (size - 4));
                if (page != _page)
                {
                    file.Position = checked(page * size); file.ReadExactly(_buffer);
                    uint crc = uint.MaxValue;
                    for (int i = 0; i < size - 4; i++)
                    {
                        crc ^= _buffer[i];
                        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0x82f63b78u : 0);
                    }
                    if (~crc != BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(size - 4)))
                        throw new InvalidDataException($"E57 checksum failed on page {page}.");
                    _page = page;
                }
                int take = Math.Min(count - written, size - 4 - offset);
                _buffer.AsSpan(offset, take).CopyTo(result.AsSpan(written));
                logical += take; written += take;
            }
            return result;
        }
    }
}
