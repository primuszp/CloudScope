using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>One backend-neutral, screen-constant visual language for object grips.</summary>
public static class GripOverlayGeometry
{
    /// <summary>Half-size of a standard CAD grip in logical pixels.</summary>
    public const float RadiusPixels = 7f;
    public const int VerticesPerGrip = 16;
    public const int FloatsPerGrip = VerticesPerGrip * 3;

    public static int Fill(
        IReadOnlyList<GripDescriptor> grips,
        OrbitCamera camera,
        Span<float> vertices,
        float displayScale = 1f)
    {
        int count = Math.Min(grips.Count, vertices.Length / FloatsPerGrip);
        Span<Vector3> shape = stackalloc Vector3[VerticesPerGrip];
        for (int i = 0; i < count; i++)
        {
            GripDescriptor grip = grips[i];
            float logicalToPhysical = float.IsFinite(displayScale) && displayScale > 0f
                ? displayScale : 1f;
            float radius = MathF.Max(
                camera.WorldUnitsPerPixel(grip.Position) * RadiusPixels * logicalToPhysical,
                0.00001f);
            Vector3 right = camera.CameraRight * radius;
            Vector3 up = camera.CameraUp * radius;

            if (grip.Kind == GripKind.Center)
            {
                for (int segment = 0; segment < 8; segment++)
                {
                    float a0 = segment * MathF.PI / 4f;
                    float a1 = (segment + 1) * MathF.PI / 4f;
                    shape[segment * 2] = grip.Position
                        + right * MathF.Cos(a0) + up * MathF.Sin(a0);
                    shape[segment * 2 + 1] = grip.Position
                        + right * MathF.Cos(a1) + up * MathF.Sin(a1);
                }
            }
            else if (grip.Kind == GripKind.Midpoint)
            {
                Vector3 top = grip.Position + up;
                Vector3 lowerRight = grip.Position + right - up;
                Vector3 lowerLeft = grip.Position - right - up;
                WriteEdge(shape, 0, top, lowerRight);
                WriteEdge(shape, 1, lowerRight, lowerLeft);
                WriteEdge(shape, 2, lowerLeft, top);
                FillDegenerate(shape, 6, grip.Position);
            }
            else if (grip.Kind is GripKind.WidthResize or GripKind.Direction or GripKind.Quadrant)
            {
                Vector3 top = grip.Position + up;
                Vector3 rightPoint = grip.Position + right;
                Vector3 bottom = grip.Position - up;
                Vector3 leftPoint = grip.Position - right;
                WriteEdge(shape, 0, top, rightPoint);
                WriteEdge(shape, 1, rightPoint, bottom);
                WriteEdge(shape, 2, bottom, leftPoint);
                WriteEdge(shape, 3, leftPoint, top);
                FillDegenerate(shape, 8, grip.Position);
            }
            else
            {
                Vector3 a = grip.Position - right + up;
                Vector3 b = grip.Position + right + up;
                Vector3 c = grip.Position + right - up;
                Vector3 d = grip.Position - right - up;
                WriteEdge(shape, 0, a, b);
                WriteEdge(shape, 1, b, c);
                WriteEdge(shape, 2, c, d);
                WriteEdge(shape, 3, d, a);
                FillDegenerate(shape, 8, grip.Position);
            }

            int vertex = i * VerticesPerGrip;
            for (int shapeVertex = 0; shapeVertex < shape.Length; shapeVertex++)
                Write(vertices, vertex + shapeVertex, shape[shapeVertex]);
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

    private static void WriteEdge(Span<Vector3> shape, int edge, Vector3 start, Vector3 end)
    {
        shape[edge * 2] = start;
        shape[edge * 2 + 1] = end;
    }

    private static void FillDegenerate(Span<Vector3> shape, int start, Vector3 point)
    {
        for (int i = start; i < shape.Length; i++) shape[i] = point;
    }
}
