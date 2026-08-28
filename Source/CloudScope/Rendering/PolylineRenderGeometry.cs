using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>Connected previous/current/next vertices for the shared anti-aliased line shader.</summary>
public static class PolylineRenderGeometry
{
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

    private static void Write(float[] destination, ref int write, Vector3 point)
    {
        destination[write++] = point.X;
        destination[write++] = point.Y;
        destination[write++] = point.Z;
    }
}
