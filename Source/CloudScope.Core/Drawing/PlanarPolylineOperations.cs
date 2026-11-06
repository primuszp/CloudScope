using OpenTK.Mathematics;

namespace CloudScope.Drawing;

internal static class PlanarPolylineOperations
{
    internal static bool TryJoin(
            PlanarPolyline selected,
            PlanarPolyline other,
            float tolerance,
            out PlanarPolyline? joined)
    {
        joined = null;
        float toleranceSquared = tolerance * tolerance;
        PlanarPolyline first = selected;
        PlanarPolyline second = other;
        bool reverseFirst = false;
        bool reverseSecond = false;

        Vector3 aStart = selected.ToWorld(selected.Vertices[0].Position);
        Vector3 aEnd = selected.Endpoint;
        Vector3 bStart = other.ToWorld(other.Vertices[0].Position);
        Vector3 bEnd = other.Endpoint;
        if (Vector3.DistanceSquared(aEnd, bStart) <= toleranceSquared) { }
        else if (Vector3.DistanceSquared(aEnd, bEnd) <= toleranceSquared) reverseSecond = true;
        else if (Vector3.DistanceSquared(aStart, bEnd) <= toleranceSquared)
        {
            first = other; second = selected;
        }
        else if (Vector3.DistanceSquared(aStart, bStart) <= toleranceSquared)
        {
            first = other; second = selected; reverseFirst = true;
        }
        else return false;

        if (reverseFirst) first = ReversedCopy(first);
        if (reverseSecond) second = ReversedCopy(second);

        var vertices = new List<PlanarPolylineVertex>();
        foreach (PlanarPolylineVertex vertex in first.Vertices)
            vertices.Add(vertex with { Position = selected.ToPlane(first.ToWorld(vertex.Position)) });
        PlanarPolylineVertex secondStart = second.Vertices[0];
        vertices[^1] = vertices[^1] with
        {
            Bulge = secondStart.Bulge,
            StartWidth = secondStart.StartWidth,
            EndWidth = secondStart.EndWidth
        };
        for (int i = 1; i < second.Vertices.Length; i++)
        {
            PlanarPolylineVertex vertex = second.Vertices[i];
            vertices.Add(vertex with { Position = selected.ToPlane(second.ToWorld(vertex.Position)) });
        }
        joined = selected with { Vertices = vertices.ToArray(), Closed = false };
        return true;
    }

    private static PlanarPolyline ReversedCopy(PlanarPolyline polyline)
    {
        var target = new PlanarPolylineGripTarget(polyline);
        target.Reverse();
        return target.Polyline.Copy();
    }

}
