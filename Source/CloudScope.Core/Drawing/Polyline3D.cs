using OpenTK.Mathematics;

namespace CloudScope.Drawing;

/// <summary>A straight-segment 3D polyline; arcs are deliberately outside this entity.</summary>
public sealed record Polyline3D(int Id, string Name, Vector3[] Vertices, bool Closed = false)
{
    public int SegmentCount => Closed ? Vertices.Length : Math.Max(Vertices.Length - 1, 0);

    public Vector3 Center
    {
        get
        {
            if (Vertices.Length == 0) return Vector3.Zero;
            Vector3 sum = Vector3.Zero;
            foreach (Vector3 vertex in Vertices) sum += vertex;
            return sum / Vertices.Length;
        }
    }

    public Polyline3D Copy() => this with { Vertices = (Vector3[])Vertices.Clone() };
}
