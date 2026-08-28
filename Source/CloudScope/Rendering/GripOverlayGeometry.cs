using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>One backend-neutral, screen-constant visual language for object grips.</summary>
public static class GripOverlayGeometry
{
    public const int VerticesPerGrip = 8;
    public const int FloatsPerGrip = VerticesPerGrip * 3;

    public static int Fill(
        IReadOnlyList<GripDescriptor> grips,
        OrbitCamera camera,
        Span<float> vertices)
    {
        int count = Math.Min(grips.Count, vertices.Length / FloatsPerGrip);
        for (int i = 0; i < count; i++)
        {
            GripDescriptor grip = grips[i];
            float radius = MathF.Max(camera.WorldUnitsPerPixel(grip.Position) * 6f, 0.00001f);
            Vector3 right = camera.CameraRight * radius;
            Vector3 up = camera.CameraUp * radius;

            Vector3 a, b, c, d;
            if (grip.Kind is GripKind.WidthResize or GripKind.Direction or GripKind.Midpoint)
            {
                a = grip.Position + up;
                b = grip.Position + right;
                c = grip.Position - up;
                d = grip.Position - right;
            }
            else
            {
                a = grip.Position - right + up;
                b = grip.Position + right + up;
                c = grip.Position + right - up;
                d = grip.Position - right - up;
            }

            int vertex = i * VerticesPerGrip;
            Write(vertices, vertex, a); Write(vertices, vertex + 1, b);
            Write(vertices, vertex + 2, b); Write(vertices, vertex + 3, c);
            Write(vertices, vertex + 4, c); Write(vertices, vertex + 5, d);
            Write(vertices, vertex + 6, d); Write(vertices, vertex + 7, a);
        }
        return count;
    }

    public static Vector4 Color(int index, int hovered, int active) => index == active
        ? new Vector4(1f, 0.56f, 0.08f, 1f)
        : index == hovered
            ? new Vector4(1f, 0.20f, 0.12f, 1f)
            : new Vector4(0.10f, 0.72f, 1f, 1f);

    public static Vector4 SnapColor(ObjectSnapKind kind, float alpha = 1f) => kind switch
    {
        ObjectSnapKind.AxisX => new Vector4(1f, 0.28f, 0.24f, alpha),
        ObjectSnapKind.AxisY => new Vector4(0.28f, 1f, 0.38f, alpha),
        ObjectSnapKind.AxisZ => new Vector4(0.30f, 0.55f, 1f, alpha),
        _ => new Vector4(0.20f, 1f, 0.42f, alpha)
    };

    private static void Write(Span<float> vertices, int vertex, Vector3 point)
    {
        int offset = vertex * 3;
        vertices[offset] = point.X;
        vertices[offset + 1] = point.Y;
        vertices[offset + 2] = point.Z;
    }
}
