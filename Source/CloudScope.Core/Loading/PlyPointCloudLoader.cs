using System.Globalization;
using System.Text;

namespace CloudScope.Loading;

/// <summary>Reads vertex-only point data from ASCII or little-endian binary PLY files.</summary>
public static class PlyPointCloudLoader
{
    private readonly record struct Property(string Name, string Type);

    public static LoadedPointCloud Load(string path, long maxPoints = 0, IProgress<int>? progress = null)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadLine(reader) != "ply") throw new InvalidDataException("Not a PLY file.");

        string? format = null;
        long vertexCount = -1;
        bool inVertex = false;
        var properties = new List<Property>();
        for (int lines = 0; lines < 10000; lines++)
        {
            string line = ReadLine(reader) ?? throw new InvalidDataException("PLY header is incomplete.");
            if (line == "end_header") break;
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] is "comment" or "obj_info") continue;
            if (parts[0] == "format" && parts.Length >= 3)
            {
                format = parts[1];
                if (parts[2] != "1.0") throw new NotSupportedException("Only PLY version 1.0 is supported.");
            }
            else if (parts[0] == "element" && parts.Length == 3)
            {
                inVertex = parts[1] == "vertex";
                if (inVertex) vertexCount = long.Parse(parts[2], CultureInfo.InvariantCulture);
                else if (vertexCount < 0 && long.Parse(parts[2], CultureInfo.InvariantCulture) > 0)
                    throw new NotSupportedException("PLY elements before vertices are not supported.");
            }
            else if (parts[0] == "property" && inVertex)
            {
                if (parts.Length != 3 || parts[1] == "list")
                    throw new NotSupportedException("Vertex list properties are not supported.");
                properties.Add(new Property(parts[2].ToLowerInvariant(), parts[1]));
            }
            if (lines == 9999) throw new InvalidDataException("PLY header is too long.");
        }

        if (format is not ("ascii" or "binary_little_endian"))
            throw new NotSupportedException("Only ASCII and little-endian binary PLY are supported.");
        if (vertexCount < 0 || vertexCount > int.MaxValue)
            throw new InvalidDataException("Invalid or unsupported PLY vertex count.");
        if (!Has("x") || !Has("y") || !Has("z"))
            throw new InvalidDataException("PLY vertices must contain x, y and z properties.");

        int count = (int)(maxPoints > 0 ? Math.Min(maxPoints, vertexCount) : vertexCount);
        var coordinates = new (double X, double Y, double Z)[count];
        var colors = new (double R, double G, double B)[count];
        var classes = new byte[count];
        var intensities = new ushort[count];
        var returns = new byte[count];
        var heights = new double[count];
        bool hasColor = Has("red") && Has("green") && Has("blue");
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        int lastPercent = -1;

        for (int i = 0; i < count; i++)
        {
            string[]? fields = null;
            if (format == "ascii")
            {
                string line = ReadLine(reader) ?? throw new EndOfStreamException("PLY vertex data is truncated.");
                fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != properties.Count) throw new InvalidDataException($"PLY vertex {i} has the wrong property count.");
            }

            double x = 0, y = 0, z = 0, red = 0, green = 0, blue = 0;
            for (int p = 0; p < properties.Count; p++)
            {
                Property property = properties[p];
                double value = fields == null
                    ? ReadNumber(reader, property.Type)
                    : double.Parse(fields[p], CultureInfo.InvariantCulture);
                switch (property.Name)
                {
                    case "x": x = value; break;
                    case "y": y = value; break;
                    case "z": z = value; break;
                    case "red": red = value; break;
                    case "green": green = value; break;
                    case "blue": blue = value; break;
                    case "classification": classes[i] = checked((byte)value); break;
                    case "intensity": intensities[i] = checked((ushort)value); break;
                    case "return_number": returns[i] = checked((byte)value); break;
                }
            }
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                throw new InvalidDataException($"PLY vertex {i} has non-finite coordinates.");
            coordinates[i] = (x, y, z);
            colors[i] = (red, green, blue);
            heights[i] = z;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
            int percent = (int)((i + 1L) * 100 / Math.Max(count, 1));
            if (percent / 5 != lastPercent / 5) { lastPercent = percent; progress?.Report(percent); }
        }

        if (count == 0) throw new InvalidDataException("PLY contains no vertices.");
        double cx = minX + (maxX - minX) / 2, cy = minY + (maxY - minY) / 2, cz = minZ + (maxZ - minZ) / 2;
        double colorMax = 0;
        if (hasColor)
            foreach (var c in colors) colorMax = Math.Max(colorMax, Math.Max(c.R, Math.Max(c.G, c.B)));
        float colorScale = colorMax <= 1 ? 1f : colorMax <= 255 ? 1f / 255 : 1f / 65535;
        var points = new PointData[count];
        for (int i = 0; i < count; i++)
        {
            var xyz = coordinates[i];
            points[i].X = (float)(xyz.X - cx);
            points[i].Y = (float)(xyz.Y - cy);
            points[i].Z = (float)(xyz.Z - cz);
            if (hasColor)
            {
                var rgb = colors[i];
                points[i].R = Math.Clamp((float)rgb.R * colorScale, 0, 1);
                points[i].G = Math.Clamp((float)rgb.G * colorScale, 0, 1);
                points[i].B = Math.Clamp((float)rgb.B * colorScale, 0, 1);
            }
            else
            {
                float t = maxZ > minZ ? (float)((xyz.Z - minZ) / (maxZ - minZ)) : 0.5f;
                points[i].R = t;
                points[i].G = 1f - MathF.Abs(2f * t - 1f);
                points[i].B = 1f - t;
            }
        }
        float dx = (float)((maxX - minX) / 2), dy = (float)((maxY - minY) / 2), dz = (float)((maxZ - minZ) / 2);
        float radius = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        return new LoadedPointCloud(points, count, radius, hasColor, colorScale, cx, cy, cz,
            new PointCloudAttributes(classes, intensities, returns, heights, minZ, maxZ));

        bool Has(string name) => properties.Any(p => p.Name == name);
    }

    private static string? ReadLine(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        while (true)
        {
            int value = reader.BaseStream.ReadByte();
            if (value < 0) return bytes.Length == 0 ? null : throw new EndOfStreamException();
            if (value == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.WriteByte((byte)value);
            if (bytes.Length > 1_000_000) throw new InvalidDataException("PLY line is too long.");
        }
    }

    private static double ReadNumber(BinaryReader reader, string type) => type switch
    {
        "char" or "int8" => reader.ReadSByte(),
        "uchar" or "uint8" => reader.ReadByte(),
        "short" or "int16" => reader.ReadInt16(),
        "ushort" or "uint16" => reader.ReadUInt16(),
        "int" or "int32" => reader.ReadInt32(),
        "uint" or "uint32" => reader.ReadUInt32(),
        "float" or "float32" => reader.ReadSingle(),
        "double" or "float64" => reader.ReadDouble(),
        _ => throw new NotSupportedException($"Unsupported PLY property type: {type}")
    };
}
