using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>
/// Expands polyline points into the instance streams consumed by the shared joined-line
/// shaders (one stream of segments, one of joins). The layout is identical on OpenGL and
/// Metal, so both backends draw a polyline — and a gizmo circle — from the same data.
/// </summary>
/// <remarks>
/// The decomposition follows the standard overlap-free construction for alpha-blended thick
/// lines (Reusser/wwwtyro, "Instanced Line Rendering Part II"): every segment quad stops at
/// the miter intersection on the inside of a bend and at the plain normal offset on the
/// outside, and the remaining outer wedge is filled by a separate round-join instance. No
/// two triangles overlap, so a translucent polyline shows no darkened knots at its joints,
/// and no mitered strip can fold over when a 3D bend projects to a sharp screen angle.
/// </remarks>
public static class PolylineRenderGeometry
{
    /// <summary>previous, start, end, next — one instance per segment.</summary>
    public const int SegmentFloats = 12;

    /// <summary>previous, joint, next — one instance per interior joint.</summary>
    public const int JoinFloats = 9;

    /// <summary>Triangles in the round-join fan. Eight is smooth well past 8 px wide lines.</summary>
    public const int JoinArcSegments = 8;

    /// <summary>Triangle-list vertices emitted per join instance.</summary>
    public const int VerticesPerJoin = JoinArcSegments * 3;

    /// <summary>Triangle-strip vertices emitted per segment instance.</summary>
    public const int VerticesPerSegment = 4;

    public static int SegmentCount(int pointCount, bool closed)
    {
        if (pointCount < 2) return 0;
        return closed && pointCount > 2 ? pointCount : pointCount - 1;
    }

    public static int JoinCount(int pointCount, bool closed)
    {
        if (pointCount < 3) return 0;
        return closed ? pointCount : pointCount - 2;
    }

    public static float[] BuildSegmentInstances(IReadOnlyList<Vector3> points, bool closed)
    {
        int count = SegmentCount(points.Count, closed);
        if (count == 0) return [];
        var instances = new float[count * SegmentFloats];
        FillSegmentInstances(points, closed, instances);
        return instances;
    }

    public static float[] BuildJoinInstances(IReadOnlyList<Vector3> points, bool closed)
    {
        int count = JoinCount(points.Count, closed);
        if (count == 0) return [];
        var instances = new float[count * JoinFloats];
        FillJoinInstances(points, closed, instances);
        return instances;
    }

    public static float[] BuildSegmentInstances(float[] xyzPoints, int pointCount, bool closed)
    {
        int count = SegmentCount(pointCount, closed);
        if (count == 0) return [];
        var instances = new float[count * SegmentFloats];
        FillSegmentInstances(xyzPoints, pointCount, closed, instances);
        return instances;
    }

    public static float[] BuildJoinInstances(float[] xyzPoints, int pointCount, bool closed)
    {
        int count = JoinCount(pointCount, closed);
        if (count == 0) return [];
        var instances = new float[count * JoinFloats];
        FillJoinInstances(xyzPoints, pointCount, closed, instances);
        return instances;
    }

    /// <summary>
    /// Writes one <see cref="SegmentFloats"/>-float instance per segment. A segment that
    /// starts or ends the polyline repeats its own endpoint in the neighbour slot; the
    /// shader reads that as "terminal end" and rounds the cap there instead of joining.
    /// </summary>
    public static int FillSegmentInstances(IReadOnlyList<Vector3> points, bool closed, Span<float> destination)
    {
        int pointCount = points.Count;
        int count = SegmentCount(pointCount, closed);
        if (count == 0) return 0;
        Require(destination, count * SegmentFloats);

        int write = 0;
        for (int segment = 0; segment < count; segment++)
        {
            int start = segment;
            int end = (segment + 1) % pointCount;
            Write(destination, ref write, points[PreviousIndex(start, pointCount, closed)]);
            Write(destination, ref write, points[start]);
            Write(destination, ref write, points[end]);
            Write(destination, ref write, points[NextIndex(end, pointCount, closed)]);
        }
        return count;
    }

    public static int FillSegmentInstances(float[] xyzPoints, int pointCount, bool closed, Span<float> destination)
    {
        int count = SegmentCount(pointCount, closed);
        if (count == 0) return 0;
        RequireSource(xyzPoints, pointCount);
        Require(destination, count * SegmentFloats);

        int write = 0;
        for (int segment = 0; segment < count; segment++)
        {
            int start = segment;
            int end = (segment + 1) % pointCount;
            Copy(xyzPoints, PreviousIndex(start, pointCount, closed), destination, ref write);
            Copy(xyzPoints, start, destination, ref write);
            Copy(xyzPoints, end, destination, ref write);
            Copy(xyzPoints, NextIndex(end, pointCount, closed), destination, ref write);
        }
        return count;
    }

    /// <summary>Writes one <see cref="JoinFloats"/>-float instance per interior joint.</summary>
    public static int FillJoinInstances(IReadOnlyList<Vector3> points, bool closed, Span<float> destination)
    {
        int pointCount = points.Count;
        int count = JoinCount(pointCount, closed);
        if (count == 0) return 0;
        Require(destination, count * JoinFloats);

        int write = 0;
        for (int join = 0; join < count; join++)
        {
            int joint = closed ? join : join + 1;
            Write(destination, ref write, points[(joint + pointCount - 1) % pointCount]);
            Write(destination, ref write, points[joint]);
            Write(destination, ref write, points[(joint + 1) % pointCount]);
        }
        return count;
    }

    public static int FillJoinInstances(float[] xyzPoints, int pointCount, bool closed, Span<float> destination)
    {
        int count = JoinCount(pointCount, closed);
        if (count == 0) return 0;
        RequireSource(xyzPoints, pointCount);
        Require(destination, count * JoinFloats);

        int write = 0;
        for (int join = 0; join < count; join++)
        {
            int joint = closed ? join : join + 1;
            Copy(xyzPoints, (joint + pointCount - 1) % pointCount, destination, ref write);
            Copy(xyzPoints, joint, destination, ref write);
            Copy(xyzPoints, (joint + 1) % pointCount, destination, ref write);
        }
        return count;
    }

    private static int PreviousIndex(int start, int pointCount, bool closed)
        => start > 0 ? start - 1 : closed ? pointCount - 1 : start;

    private static int NextIndex(int end, int pointCount, bool closed)
        => closed ? (end + 1) % pointCount : end < pointCount - 1 ? end + 1 : end;

    private static void Require(Span<float> destination, int floats)
    {
        if (destination.Length < floats)
            throw new ArgumentException("Destination is too short for the polyline instances.", nameof(destination));
    }

    private static void RequireSource(float[] xyzPoints, int pointCount)
    {
        if (xyzPoints.Length < pointCount * 3)
            throw new ArgumentException("Point buffer is shorter than pointCount.", nameof(xyzPoints));
    }

    private static void Write(Span<float> destination, ref int write, Vector3 point)
    {
        destination[write++] = point.X;
        destination[write++] = point.Y;
        destination[write++] = point.Z;
    }

    private static void Copy(float[] source, int point, Span<float> destination, ref int write)
    {
        int read = point * 3;
        destination[write++] = source[read];
        destination[write++] = source[read + 1];
        destination[write++] = source[read + 2];
    }
}
