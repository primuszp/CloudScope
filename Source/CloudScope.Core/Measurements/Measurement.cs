using OpenTK.Mathematics;

namespace CloudScope.Measurements;

public enum MeasurementKind { Distance, Area }

/// <summary>Picked positions in the document frame. Units follow the survey's coordinates.</summary>
public sealed record Measurement(int Id, string Name, MeasurementKind Kind, Vector3[] Points)
{
    public MeasurementResult Evaluate()
    {
        if (Id <= 0 || Id == int.MaxValue || string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Kind)
            || Points == null || (Kind == MeasurementKind.Distance ? Points.Length != 2 : Points.Length < 3)
            || Points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
            throw new InvalidDataException("Invalid measurement geometry or identity.");
        if (Kind == MeasurementKind.Distance)
        {
            double dx = (double)Points[1].X - Points[0].X, dy = (double)Points[1].Y - Points[0].Y;
            double dz = (double)Points[1].Z - Points[0].Z;
            double horizontal = Math.Sqrt(dx * dx + dy * dy);
            return new(Math.Sqrt(horizontal * horizontal + dz * dz), horizontal, dz,
                horizontal == 0 ? null : 100 * dz / horizontal, 0, Math.Atan2(dz, horizontal) * 180 / Math.PI);
        }
        // Translate to the first vertex before the shoelace sum to avoid subtracting
        // large world-coordinate products. Area is explicitly the horizontal projection.
        double twiceArea = 0, perimeter = 0;
        for (int i = 0; i < Points.Length; i++)
        {
            var a = Points[i]; var b = Points[(i + 1) % Points.Length];
            double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y;
            if (dx == 0 && dy == 0) throw new InvalidDataException("Area vertices must be distinct in plan.");
            perimeter += Math.Sqrt(dx * dx + dy * dy);
            twiceArea += ((double)a.X - Points[0].X) * ((double)b.Y - Points[0].Y)
                - ((double)b.X - Points[0].X) * ((double)a.Y - Points[0].Y);
            for (int j = i + 2; j < Points.Length; j++)
            {
                if (i == 0 && j == Points.Length - 1) continue;
                if (Intersects(a, b, Points[j], Points[(j + 1) % Points.Length]))
                    throw new InvalidDataException("Area boundary must not intersect or touch itself.");
            }
        }
        if (twiceArea == 0) throw new InvalidDataException("Area boundary is degenerate.");
        return new(perimeter, perimeter, 0, null, Math.Abs(twiceArea) * .5, 0);
    }

    private static bool Intersects(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        static double Side(Vector3 p, Vector3 q, Vector3 r) =>
            ((double)q.X - p.X) * ((double)r.Y - p.Y) - ((double)q.Y - p.Y) * ((double)r.X - p.X);
        static bool On(Vector3 p, Vector3 q, Vector3 r) => r.X >= Math.Min(p.X,q.X) && r.X <= Math.Max(p.X,q.X)
            && r.Y >= Math.Min(p.Y,q.Y) && r.Y <= Math.Max(p.Y,q.Y);
        double s1 = Side(a,b,c), s2 = Side(a,b,d), s3 = Side(c,d,a), s4 = Side(c,d,b);
        return s1 == 0 && On(a,b,c) || s2 == 0 && On(a,b,d) || s3 == 0 && On(c,d,a) || s4 == 0 && On(c,d,b)
            || s1 * s2 < 0 && s3 * s4 < 0;
    }
}

public readonly record struct MeasurementResult(double Spatial, double Horizontal, double DeltaZ, double? SlopePercent,
    double Area, double AngleDegrees);
