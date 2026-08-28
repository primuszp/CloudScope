using OpenTK.Mathematics;

namespace CloudScope.Rendering;

internal readonly record struct PivotLineBatch(float[] Positions, Vector3 Color, bool IsClosedLoop = false)
{
    public int PointCount => Positions.Length / 3;
}

internal static class PivotIndicatorGeometry
{
    public const int RingSegments = 96;

    public static PivotLineBatch[] BuildBatches()
    {
        const float axisExtent = 0.55f;
        return
        [
            new(BuildAxis(0, axisExtent), new Vector3(1f, 0.3f, 0.3f)),
            new(BuildAxis(1, axisExtent), new Vector3(0.3f, 1f, 0.3f)),
            new(BuildAxis(2, axisExtent), new Vector3(0.3f, 0.5f, 1f)),
            new(BuildRing(0), new Vector3(1f, 0.35f, 0.35f), IsClosedLoop: true),
            new(BuildRing(1), new Vector3(0.35f, 1f, 0.35f), IsClosedLoop: true),
            new(BuildRing(2), new Vector3(0.35f, 0.6f, 1f), IsClosedLoop: true)
        ];
    }

    /// <summary>
    /// Expands unique loop points into the shared joined-line instance streams: one quad
    /// per ring segment plus one round join per point. The ring is therefore a single
    /// overlap-free surface, with no independently rasterized segment ends.
    /// </summary>
    public static float[] BuildLoopSegmentInstances(float[] points)
        => PolylineRenderGeometry.BuildSegmentInstances(points, points.Length / 3, closed: true);

    /// <inheritdoc cref="BuildLoopSegmentInstances"/>
    public static float[] BuildLoopJoinInstances(float[] points)
        => PolylineRenderGeometry.BuildJoinInstances(points, points.Length / 3, closed: true);

    private static float[] BuildAxis(int axis, float extent)
    {
        var positions = new float[6];
        positions[axis] = -extent;
        positions[3 + axis] = extent;
        return positions;
    }

    private static float[] BuildRing(int normalAxis)
    {
        var positions = new float[RingSegments * 3];
        float step = MathF.Tau / RingSegments;
        int destination = 0;
        for (int point = 0; point < RingSegments; point++)
            WriteRingPoint(positions, ref destination, normalAxis, point * step);

        return positions;
    }

    private static void WriteRingPoint(float[] positions, ref int destination, int normalAxis, float angle)
    {
        float cosine = MathF.Cos(angle);
        float sine = MathF.Sin(angle);
        if (normalAxis == 0)
        {
            positions[destination++] = 0f;
            positions[destination++] = cosine;
            positions[destination++] = sine;
        }
        else if (normalAxis == 1)
        {
            positions[destination++] = cosine;
            positions[destination++] = 0f;
            positions[destination++] = sine;
        }
        else
        {
            positions[destination++] = cosine;
            positions[destination++] = sine;
            positions[destination++] = 0f;
        }
    }
}
