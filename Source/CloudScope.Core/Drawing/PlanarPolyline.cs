using OpenTK.Mathematics;

namespace CloudScope.Drawing;

/// <summary>
/// One vertex of a planar CAD polyline. <see cref="Bulge"/> describes the outgoing segment:
/// zero is a line, otherwise it is tan(included-angle / 4), positive counter-clockwise.
/// Widths belong to that same outgoing segment.
/// </summary>
public readonly record struct PlanarPolylineVertex(
    Vector2 Position,
    float Bulge = 0f,
    float StartWidth = 0f,
    float EndWidth = 0f);

/// <summary>
/// A 2D line/arc polyline embedded in a stable world-space plane. Geometry is stored in plane
/// coordinates so every edit preserves planarity instead of accumulating projected 3D error.
/// </summary>
public sealed record PlanarPolyline(
    int Id,
    string Name,
    Vector3 Origin,
    Vector3 AxisX,
    Vector3 AxisY,
    PlanarPolylineVertex[] Vertices,
    bool Closed = false)
{
    public int SegmentCount => Closed ? Vertices.Length : Math.Max(Vertices.Length - 1, 0);
    public Vector3 Normal => Vector3.Cross(AxisX, AxisY).Normalized();

    public Vector3 ToWorld(Vector2 point) => Origin + AxisX * point.X + AxisY * point.Y;

    public Vector2 ToPlane(Vector3 point)
    {
        Vector3 delta = point - Origin;
        return new Vector2(Vector3.Dot(delta, AxisX), Vector3.Dot(delta, AxisY));
    }

    public Vector3 Endpoint => Vertices.Length == 0 ? Origin : ToWorld(Vertices[^1].Position);

    public PlanarPolyline Copy() => this with { Vertices = (PlanarPolylineVertex[])Vertices.Clone() };
}

/// <summary>Shared line/arc sampling used by both GPU backends and by interaction tests.</summary>
public static class PlanarPolylineGeometry
{
    public const float DefaultMaxAngleStepDegrees = 5f;

    public static Vector3[] Tessellate(
        PlanarPolyline polyline,
        float maxAngleStepDegrees = DefaultMaxAngleStepDegrees)
    {
        if (polyline.Vertices.Length == 0)
            return [];

        var points = new List<Vector3> { polyline.ToWorld(polyline.Vertices[0].Position) };
        for (int segment = 0; segment < polyline.SegmentCount; segment++)
        {
            int next = (segment + 1) % polyline.Vertices.Length;
            AppendSegment(polyline, polyline.Vertices[segment], polyline.Vertices[next],
                maxAngleStepDegrees, points);
        }
        return points.ToArray();
    }

    public static bool HasWidth(PlanarPolyline polyline) =>
        polyline.Vertices.Any(vertex => vertex.StartWidth > 1e-6f || vertex.EndWidth > 1e-6f);

    public static bool TryGetArc(
        PlanarPolylineVertex start,
        PlanarPolylineVertex end,
        out Vector2 center,
        out float radius,
        out float startAngle,
        out float sweep)
    {
        center = default;
        radius = 0f;
        startAngle = 0f;
        sweep = 0f;
        float bulge = start.Bulge;
        Vector2 chord = end.Position - start.Position;
        float chordLength = chord.Length;
        if (MathF.Abs(bulge) < 1e-6f || chordLength < 1e-6f)
            return false;

        sweep = 4f * MathF.Atan(bulge);
        Vector2 left = new(-chord.Y / chordLength, chord.X / chordLength);
        float centerOffset = chordLength * (1f - bulge * bulge) / (4f * bulge);
        center = (start.Position + end.Position) * 0.5f + left * centerOffset;
        Vector2 radial = start.Position - center;
        radius = radial.Length;
        startAngle = MathF.Atan2(radial.Y, radial.X);
        return float.IsFinite(radius) && radius > 1e-6f;
    }

    public static Vector2 SegmentMidpoint(PlanarPolylineVertex start, PlanarPolylineVertex end)
    {
        if (!TryGetArc(start, end, out Vector2 center, out float radius,
                out float startAngle, out float sweep))
            return (start.Position + end.Position) * 0.5f;
        float angle = startAngle + sweep * 0.5f;
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    public static bool ContainsArcAngle(float startAngle, float sweep, float angle)
    {
        static float Positive(float value)
        {
            float period = 2f * MathF.PI;
            value %= period;
            return value < 0f ? value + period : value;
        }

        return sweep >= 0f
            ? Positive(angle - startAngle) <= sweep + 1e-5f
            : Positive(startAngle - angle) <= -sweep + 1e-5f;
    }

    /// <summary>
    /// Tessellates the two world-space edges of a wide polyline. The same sampled centerline
    /// drives both render backends, including tapered arc segments.
    /// </summary>
    public static (Vector3[] Left, Vector3[] Right) TessellateWidthEdges(
        PlanarPolyline polyline,
        float maxAngleStepDegrees = DefaultMaxAngleStepDegrees)
    {
        if (polyline.SegmentCount == 0)
            return ([], []);

        var centers = new List<Vector3>();
        var widths = new List<float>();
        for (int segment = 0; segment < polyline.SegmentCount; segment++)
        {
            int next = (segment + 1) % polyline.Vertices.Length;
            PlanarPolylineVertex start = polyline.Vertices[segment];
            PlanarPolylineVertex end = polyline.Vertices[next];
            var segmentPoints = new List<Vector3> { polyline.ToWorld(start.Position) };
            AppendSegment(polyline, start, end, maxAngleStepDegrees, segmentPoints);
            int first = segment == 0 ? 0 : 1;
            for (int i = first; i < segmentPoints.Count; i++)
            {
                float t = segmentPoints.Count == 1 ? 0f : (float)i / (segmentPoints.Count - 1);
                centers.Add(segmentPoints[i]);
                widths.Add(MathHelper.Lerp(start.StartWidth, start.EndWidth, t));
            }
        }

        var left = new Vector3[centers.Count];
        var right = new Vector3[centers.Count];
        for (int i = 0; i < centers.Count; i++)
        {
            int previous = Math.Max(i - 1, 0);
            int next = Math.Min(i + 1, centers.Count - 1);
            Vector3 tangent = centers[next] - centers[previous];
            if (tangent.LengthSquared < 1e-10f) tangent = polyline.AxisX;
            tangent.Normalize();
            Vector3 side = Vector3.Cross(polyline.Normal, tangent);
            if (side.LengthSquared < 1e-10f) side = polyline.AxisY;
            side.Normalize();
            Vector3 offset = side * (widths[i] * 0.5f);
            left[i] = centers[i] + offset;
            right[i] = centers[i] - offset;
        }
        return (left, right);
    }

    private static void AppendSegment(
        PlanarPolyline polyline,
        PlanarPolylineVertex start,
        PlanarPolylineVertex end,
        float maxAngleStepDegrees,
        List<Vector3> destination)
    {
        if (!TryGetArc(start, end, out Vector2 center, out float arcRadius,
                out float startAngle, out float sweep))
        {
            destination.Add(polyline.ToWorld(end.Position));
            return;
        }
        float maxStep = MathF.Max(maxAngleStepDegrees, 0.25f) * MathF.PI / 180f;
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweep) / maxStep));
        for (int i = 1; i <= steps; i++)
        {
            float angle = startAngle + sweep * i / steps;
            Vector2 point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * arcRadius;
            destination.Add(polyline.ToWorld(point));
        }
    }

    /// <summary>
    /// Returns the bulge of the arc from <paramref name="start"/> to <paramref name="end"/>
    /// that leaves the start along <paramref name="startTangent"/>. Degenerate geometry falls
    /// back to a line.
    /// </summary>
    public static float TangentArcBulge(Vector2 start, Vector2 end, Vector2 startTangent)
    {
        Vector2 chord = end - start;
        if (chord.LengthSquared < 1e-10f || startTangent.LengthSquared < 1e-10f)
            return 0f;

        Vector2 tangent = startTangent.Normalized();
        float cross = tangent.X * chord.Y - tangent.Y * chord.X;
        float dot = Vector2.Dot(tangent, chord);
        float angle = 2f * MathF.Atan2(cross, dot);
        if (angle > MathF.PI) angle -= 2f * MathF.PI;
        if (angle < -MathF.PI) angle += 2f * MathF.PI;
        return MathF.Abs(angle) < 1e-5f ? 0f : MathF.Tan(angle * 0.25f);
    }

    public static float IncludedAngleBulge(float angleDegrees) =>
        MathF.Tan(angleDegrees * MathF.PI / 720f);

    public static float CenterArcBulge(Vector2 start, Vector2 end, Vector2 center, float preferredSign = 1f)
    {
        Vector2 a = start - center;
        Vector2 b = end - center;
        if (a.LengthSquared < 1e-10f || b.LengthSquared < 1e-10f) return 0f;
        float cross = a.X * b.Y - a.Y * b.X;
        float dot = Vector2.Dot(a, b);
        float sweep = MathF.Atan2(cross, dot);
        if (preferredSign < 0f && sweep > 0f) sweep -= 2f * MathF.PI;
        if (preferredSign >= 0f && sweep < 0f) sweep += 2f * MathF.PI;
        return MathF.Tan(sweep * 0.25f);
    }

    public static float ThreePointArcBulge(Vector2 start, Vector2 through, Vector2 end)
    {
        float ax = start.X, ay = start.Y;
        float bx = through.X, by = through.Y;
        float cx = end.X, cy = end.Y;
        float d = 2f * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (MathF.Abs(d) < 1e-7f) return 0f;
        float aa = ax * ax + ay * ay;
        float bb = bx * bx + by * by;
        float cc = cx * cx + cy * cy;
        Vector2 center = new(
            (aa * (by - cy) + bb * (cy - ay) + cc * (ay - by)) / d,
            (aa * (cx - bx) + bb * (ax - cx) + cc * (bx - ax)) / d);
        float orientation = (end.X - start.X) * (through.Y - start.Y)
                          - (end.Y - start.Y) * (through.X - start.X);
        // A point to the left of the directed chord selects the clockwise-side centerline
        // traversal, hence the sign is opposite the chord/through-point orientation.
        return CenterArcBulge(start, end, center, orientation <= 0f ? 1f : -1f);
    }

    public static float RadiusArcBulge(
        Vector2 start, Vector2 end, float radius, Vector2 preferredTangent)
    {
        float chord = Vector2.Distance(start, end);
        if (radius <= 0f || chord < 1e-6f || chord > 2f * radius + 1e-5f) return 0f;
        float sweep = 2f * MathF.Asin(Math.Clamp(chord / (2f * radius), 0f, 1f));
        Vector2 delta = end - start;
        float sign = preferredTangent.X * delta.Y - preferredTangent.Y * delta.X;
        if (sign < 0f) sweep = -sweep;
        return MathF.Tan(sweep * 0.25f);
    }
}
