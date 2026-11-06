using System.Globalization;
using System.Text;
using CloudScope.IO;
using CloudScope.Sections;
using OpenTK.Mathematics;

namespace CloudScope.Measurements;

public enum EngineeringExportFormat { Csv, Dxf }
public readonly record struct SectionPoint(int Layer, long Index, Vector3 Position, double X, double Y, double Z);

/// <summary>Atomic, culture-independent exchange of engineering geometry and derived values.</summary>
public static class EngineeringExport
{
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    public static void Measurements(string path, EngineeringExportFormat format, IReadOnlyList<Measurement> measurements,
        (double X, double Y, double Z) origin)
    {
        var results = measurements.Select(m => m.Evaluate()).ToArray();
        AtomicFile.Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            if (format == EngineeringExportFormat.Csv)
            {
                writer.WriteLine("id,name,kind,spatial_or_plan_perimeter,horizontal,delta_z,slope_percent,angle_degrees,area_xy");
                for (int i = 0; i < measurements.Count; i++)
                {
                    var m = measurements[i]; var r = results[i];
                    writer.WriteLine($"{m.Id},{Csv(m.Name)},{m.Kind},{N(r.Spatial)},{N(r.Horizontal)},{N(r.DeltaZ)},{(r.SlopePercent is { } slope ? N(slope) : "")},{N(r.AngleDegrees)},{N(r.Area)}");
                }
            }
            else
            {
                var dxf = new Dxf(writer);
                foreach (var m in measurements)
                {
                    dxf.Group(0, "POLYLINE"); dxf.Group(8, "MEASUREMENTS"); dxf.Group(66, 1);
                    dxf.Group(70, m.Kind == MeasurementKind.Area ? 9 : 8); dxf.Position(0, 0, 0);
                    foreach (var p in m.Points)
                    {
                        dxf.Group(0, "VERTEX"); dxf.Group(8, "MEASUREMENTS"); dxf.Group(70, 32);
                        dxf.Position(p.X + origin.X, p.Y + origin.Y, p.Z + origin.Z);
                    }
                    dxf.Group(0, "SEQEND"); dxf.Group(8, "MEASUREMENTS");
                    var r = m.Evaluate(); var first = m.Points[0];
                    dxf.Text(m.Name + (m.Kind == MeasurementKind.Area ? " area XY=" + N(r.Area) : " distance=" + N(r.Spatial)),
                        first.X + origin.X, first.Y + origin.Y, first.Z + origin.Z);
                }
                dxf.End();
            }
        });
    }

    public static long Section(string path, EngineeringExportFormat format, SectionDefinition section,
        IEnumerable<SectionPoint> points, CancellationToken cancellation = default)
    {
        long count = 0;
        AtomicFile.Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            Dxf? dxf = format == EngineeringExportFormat.Dxf ? new(writer) : null;
            if (dxf == null) writer.WriteLine("section,layer,point_index,station,elevation,offset,x,y,z");
            foreach (var p in points)
            {
                cancellation.ThrowIfCancellationRequested();
                Vector3 delta = p.Position - section.Start;
                double station = (double)delta.X * section.Along.X + (double)delta.Y * section.Along.Y;
                double offset = (double)delta.X * section.Normal.X + (double)delta.Y * section.Normal.Y;
                if (dxf == null)
                    writer.WriteLine($"{Csv(section.Name)},{p.Layer},{p.Index},{N(station)},{N(p.Z)},{N(offset)},{N(p.X)},{N(p.Y)},{N(p.Z)}");
                else { dxf.Group(0, "POINT"); dxf.Group(8, "SECTION_POINTS"); dxf.Position(station, p.Z, 0); }
                count++;
            }
            if (dxf != null) { dxf.Text(section.Name, 0, section.Start.Z, 0); dxf.End(); }
            cancellation.ThrowIfCancellationRequested();
        });
        return count;
    }

    // ASCII DXF R12, explicit 3D POLYLINE/VERTEX or POINT entities. No implicit units.
    private sealed class Dxf
    {
        private readonly TextWriter _writer;
        public Dxf(TextWriter writer)
        {
            _writer = writer;
            Group(0, "SECTION"); Group(2, "HEADER"); Group(9, "$ACADVER"); Group(1, "AC1009");
            Group(0, "ENDSEC"); Group(0, "SECTION"); Group(2, "ENTITIES");
        }
        public void Group(int code, object value) { _writer.WriteLine(code.ToString(CultureInfo.InvariantCulture)); _writer.WriteLine(value); }
        public void Position(double x, double y, double z) { Group(10, N(x)); Group(20, N(y)); Group(30, N(z)); }
        public void Text(string value, double x, double y, double z)
        {
            Group(0, "TEXT"); Group(8, "ANNOTATIONS"); Position(x, y, z); Group(40, "1");
            Group(1, string.Concat(value.Select(c => c is >= ' ' and <= '~' ? c : '_')));
        }
        public void End() { Group(0, "ENDSEC"); Group(0, "EOF"); }
    }
}
