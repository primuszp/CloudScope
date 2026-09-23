using System.Globalization;

namespace CloudScope.Loading;

/// <summary>Reads simple XYZ text clouds with optional intensity and RGB columns.</summary>
public static class XyzPointCloudLoader
{
    private readonly record struct Row(double X, double Y, double Z, double Intensity, double R, double G, double B);

    public static LoadedPointCloud Load(string path, long maxPoints = 0, IProgress<int>? progress = null)
    {
        var rows = new List<Row>();
        int columns = 0;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        double maxColor = 0, maxIntensity = 0;
        long limit = maxPoints > 0 ? Math.Min(maxPoints, int.MaxValue) : int.MaxValue;
        long fileLength = new FileInfo(path).Length;
        int lastPercent = -1;

        using (var stream = File.OpenRead(path))
        using (var reader = new StreamReader(stream))
        {
            string? line;
            int lineNumber = 0;
            while (rows.Count < limit && (line = reader.ReadLine()) != null)
            {
                lineNumber++;
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (columns == 0 && !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    continue; // One optional column-name row.
                if (columns == 0)
                {
                    columns = parts.Length;
                    if (columns is not (3 or 4 or 6 or 7))
                        throw new InvalidDataException("XYZ rows must have 3, 4, 6 or 7 columns.");
                }
                if (parts.Length != columns)
                    throw new InvalidDataException($"XYZ line {lineNumber} has {parts.Length} columns; expected {columns}.");

                var values = new double[columns];
                for (int i = 0; i < columns; i++)
                {
                    if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                        !double.IsFinite(values[i]))
                        throw new InvalidDataException($"XYZ line {lineNumber} has an invalid number in column {i + 1}.");
                }
                double intensity = columns is 4 or 7 ? values[3] : 0;
                int rgb = columns == 6 ? 3 : 4;
                double r = columns >= 6 ? values[rgb] : 0;
                double g = columns >= 6 ? values[rgb + 1] : 0;
                double b = columns >= 6 ? values[rgb + 2] : 0;
                if (intensity < 0 || r < 0 || g < 0 || b < 0)
                    throw new InvalidDataException($"XYZ line {lineNumber} has a negative intensity or color.");
                rows.Add(new Row(values[0], values[1], values[2], intensity, r, g, b));
                minX = Math.Min(minX, values[0]); maxX = Math.Max(maxX, values[0]);
                minY = Math.Min(minY, values[1]); maxY = Math.Max(maxY, values[1]);
                minZ = Math.Min(minZ, values[2]); maxZ = Math.Max(maxZ, values[2]);
                maxColor = Math.Max(maxColor, Math.Max(r, Math.Max(g, b)));
                maxIntensity = Math.Max(maxIntensity, intensity);
                int percent = (int)(stream.Position * 100 / Math.Max(fileLength, 1));
                if (percent / 5 != lastPercent / 5) { lastPercent = percent; progress?.Report(percent); }
            }
        }

        if (rows.Count == 0) throw new InvalidDataException("XYZ file contains no points.");
        bool hasColor = columns >= 6;
        float colorScale = maxColor <= 1 ? 1f : maxColor <= 255 ? 1f / 255 : 1f / 65535;
        double intensityScale = maxIntensity <= 1 ? 65535 : maxIntensity <= 255 ? 257 : 1;
        double cx = minX + (maxX - minX) / 2, cy = minY + (maxY - minY) / 2, cz = minZ + (maxZ - minZ) / 2;
        var points = new PointData[rows.Count];
        var classes = new byte[rows.Count];
        var intensities = new ushort[rows.Count];
        var returns = new byte[rows.Count];
        var heights = new double[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            Row row = rows[i];
            points[i].X = (float)(row.X - cx);
            points[i].Y = (float)(row.Y - cy);
            points[i].Z = (float)(row.Z - cz);
            heights[i] = row.Z;
            intensities[i] = (ushort)Math.Clamp(Math.Round(row.Intensity * intensityScale), 0, ushort.MaxValue);
            if (hasColor)
            {
                points[i].R = Math.Clamp((float)row.R * colorScale, 0, 1);
                points[i].G = Math.Clamp((float)row.G * colorScale, 0, 1);
                points[i].B = Math.Clamp((float)row.B * colorScale, 0, 1);
            }
            else
            {
                float t = maxZ > minZ ? (float)((row.Z - minZ) / (maxZ - minZ)) : 0.5f;
                points[i].R = t;
                points[i].G = 1f - MathF.Abs(2f * t - 1f);
                points[i].B = 1f - t;
            }
        }
        float dx = (float)((maxX - minX) / 2), dy = (float)((maxY - minY) / 2), dz = (float)((maxZ - minZ) / 2);
        float radius = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        return new LoadedPointCloud(points, rows.Count, radius, hasColor, colorScale, cx, cy, cz,
            new PointCloudAttributes(classes, intensities, returns, heights, minZ, maxZ));
    }
}
