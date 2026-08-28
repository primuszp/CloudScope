using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>Connected previous/current/next vertices for the shared anti-aliased line shader.</summary>
public static class PolylineRenderGeometry
{
    public static int RequiredVertexCount(int pointCount, bool closed) => pointCount < 2
        ? 0
        : closed && pointCount > 2 ? (pointCount + 1) * 2 : pointCount * 2;

    public static float[] Build(IReadOnlyList<Vector3> points, bool closed)
    {
        int pointCount = points.Count;
        if (pointCount < 2) return [];
        int outputPoints = closed && pointCount > 2 ? pointCount + 1 : pointCount;
        var vertices = new float[outputPoints * 2 * 9];
        int write = 0;
        for (int output = 0; output < outputPoints; output++)
        {
            int current = output % pointCount;
            int previous = current == 0 ? (closed ? pointCount - 1 : 0) : current - 1;
            int next = current == pointCount - 1 ? (closed ? 0 : pointCount - 1) : current + 1;
            for (int side = 0; side < 2; side++)
            {
                Write(vertices, ref write, points[previous]);
                Write(vertices, ref write, points[current]);
                Write(vertices, ref write, points[next]);
            }
        }
        return vertices;
    }

    public static float[] Build(float[] xyzPoints, int pointCount, bool closed)
    {
        int vertexCount = RequiredVertexCount(pointCount, closed);
        if (vertexCount == 0) return [];
        var vertices = new float[vertexCount * 9];
        Fill(xyzPoints, pointCount, closed, vertices);
        return vertices;
    }

    public static int Fill(
        float[] xyzPoints, int pointCount, bool closed, Span<float> destination)
    {
        int vertexCount = RequiredVertexCount(pointCount, closed);
        if (xyzPoints.Length < pointCount * 3)
            throw new ArgumentException("Point buffer is shorter than pointCount.", nameof(xyzPoints));
        if (destination.Length < vertexCount * 9)
            throw new ArgumentException("Destination is too short for expanded polyline geometry.", nameof(destination));
        if (vertexCount == 0) return 0;

        int outputPoints = vertexCount / 2;
        int write = 0;
        for (int output = 0; output < outputPoints; output++)
        {
            int current = output % pointCount;
            int previous = current == 0 ? (closed ? pointCount - 1 : 0) : current - 1;
            int next = current == pointCount - 1 ? (closed ? 0 : pointCount - 1) : current + 1;
            for (int side = 0; side < 2; side++)
            {
                Copy(xyzPoints, previous, destination, ref write);
                Copy(xyzPoints, current, destination, ref write);
                Copy(xyzPoints, next, destination, ref write);
            }
        }
        return vertexCount;
    }

    private static void Write(float[] destination, ref int write, Vector3 point)
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
