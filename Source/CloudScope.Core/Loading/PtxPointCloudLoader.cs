using System.Globalization;

namespace CloudScope.Loading;

/// <summary>Reads structured PTX scans. The file's matrix uses row-vector convention.</summary>
public static class PtxPointCloudLoader
{
    public static LoadedPointCloud Load(string path, long maxPoints = 0, IProgress<int>? progress = null)
    {
        using var reader = new StreamReader(path);
        var cloud = new PointCloudBuilder();
        long limit = maxPoints > 0 ? maxPoints : int.MaxValue;
        string? first;
        while (cloud.Count < limit && (first = Next()) != null)
        {
            int columns = int.Parse(first, CultureInfo.InvariantCulture);
            int rows = int.Parse(Next() ?? throw new EndOfStreamException(), CultureInfo.InvariantCulture);
            if (columns <= 0 || rows <= 0) throw new InvalidDataException("Invalid PTX dimensions.");
            for (int i = 0; i < 4; i++) ReadValues(3); // Position and scanner axes; matrix below is authoritative.
            var matrix = new double[16];
            for (int i = 0; i < 4; i++) ReadValues(4).CopyTo(matrix, i * 4);
            if (matrix[3] != 0 || matrix[7] != 0 || matrix[11] != 0 || matrix[15] != 1)
                throw new InvalidDataException("PTX requires an affine row-vector transform.");
            long count = checked((long)columns * rows);
            for (long i = 0; i < count; i++)
            {
                double[] p = ReadValues();
                if (p.Length is not (4 or 7)) throw new InvalidDataException("PTX point must have 4 or 7 values.");
                if (p[0] == 0 && p[1] == 0 && p[2] == 0) continue;
                if (cloud.Count >= limit) break;
                bool color = p.Length == 7;
                cloud.HasColor |= color;
                cloud.Add(p[0] * matrix[0] + p[1] * matrix[4] + p[2] * matrix[8] + matrix[12],
                    p[0] * matrix[1] + p[1] * matrix[5] + p[2] * matrix[9] + matrix[13],
                    p[0] * matrix[2] + p[1] * matrix[6] + p[2] * matrix[10] + matrix[14],
                    color ? (float)(p[4] / 255) : 0.8f, color ? (float)(p[5] / 255) : 0.8f, color ? (float)(p[6] / 255) : 0.8f,
                    (ushort)Math.Clamp(Math.Round(p[3] * 65535), 0, 65535));
            }
            progress?.Report(50);
        }
        return cloud.Build();

        string? Next()
        {
            string? line;
            while ((line = reader.ReadLine()) != null) if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
            return null;
        }
        double[] ReadValues(int expected = 0)
        {
            string line = Next() ?? throw new EndOfStreamException("Truncated PTX scan.");
            double[] values = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(v => !double.IsFinite(v)) || (expected > 0 && values.Length != expected))
                throw new InvalidDataException("Invalid PTX header or point.");
            return values;
        }
    }
}
