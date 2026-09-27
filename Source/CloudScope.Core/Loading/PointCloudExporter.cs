using System.Globalization;
using System.Text;

namespace CloudScope.Loading;

public enum PointCloudExportFormat { PlyBinary, PlyAscii, Xyz, Pts, Csv }

/// <summary>Exports the current filtered sample using source RGB and absolute coordinates.</summary>
public static class PointCloudExporter
{
    public static void Write(Stream stream, PointCloudDataset dataset, PointCloudExportFormat format,
        Func<int, byte>? classification = null)
    {
        if (format is PointCloudExportFormat.PlyBinary or PointCloudExportFormat.PlyAscii)
        {
            string header = $"ply\nformat {(format == PointCloudExportFormat.PlyBinary ? "binary_little_endian" : "ascii")} 1.0\n" +
                $"comment Exported from CloudScope\nelement vertex {dataset.VisibleCount}\n" +
                "property double x\nproperty double y\nproperty double z\n" +
                (dataset.HasColor ? "property uchar red\nproperty uchar green\nproperty uchar blue\n" : "") +
                "property ushort intensity\nproperty uchar classification\nend_header\n";
            stream.Write(Encoding.ASCII.GetBytes(header));
        }
        using var binary = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        using var text = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
        if (format == PointCloudExportFormat.Pts) text.WriteLine(dataset.VisibleCount.ToString(CultureInfo.InvariantCulture));
        if (format == PointCloudExportFormat.Csv)
            text.WriteLine("X,Y,Z,Intensity,Classification" + (dataset.HasColor ? ",R,G,B" : ""));
        for (int view = 0; view < dataset.VisibleCount; view++)
        {
            int source = dataset.ViewToSource?[view] ?? view;
            PointData point = dataset.SourcePoints[source];
            double x = point.X + dataset.OriginX, y = point.Y + dataset.OriginY, z = point.Z + dataset.OriginZ;
            ushort intensity = dataset.Attributes.Intensity[source];
            byte cls = classification?.Invoke(source) ?? dataset.Attributes.Class[source];
            byte r = Channel(point.R), g = Channel(point.G), b = Channel(point.B);
            if (format == PointCloudExportFormat.PlyBinary)
            {
                binary.Write(x); binary.Write(y); binary.Write(z);
                if (dataset.HasColor) { binary.Write(r); binary.Write(g); binary.Write(b); }
                binary.Write(intensity); binary.Write(cls);
            }
            else
            {
                string xyz = string.Create(CultureInfo.InvariantCulture, $"{x:R} {y:R} {z:R}");
                string rgb = dataset.HasColor ? $" {r} {g} {b}" : "";
                text.WriteLine(format switch
                {
                    PointCloudExportFormat.PlyAscii => xyz + rgb + $" {intensity} {cls}",
                    PointCloudExportFormat.Pts => xyz + " " + (intensity / 65535d).ToString("R", CultureInfo.InvariantCulture) + rgb,
                    PointCloudExportFormat.Csv => xyz.Replace(' ', ',') + $",{intensity},{cls}" + rgb.Replace(' ', ','),
                    _ => xyz + rgb
                });
            }
        }
    }

    private static byte Channel(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
}
