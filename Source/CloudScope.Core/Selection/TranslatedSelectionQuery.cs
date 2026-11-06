using OpenTK.Mathematics;

namespace CloudScope.Selection;

/// <summary>Adapts a document-space query to source-local coordinates without copying source points.</summary>
public sealed class TranslatedSelectionQuery(IPointSelectionQuery documentQuery, Vector3 offset) : IPointSelectionQuery
{
    public bool IsEmpty => documentQuery.IsEmpty;
    public bool Contains(float x, float y, float z) => documentQuery.Contains(x + offset.X, y + offset.Y, z + offset.Z);
    public void GetBounds(out Vector3 min, out Vector3 max)
    { documentQuery.GetBounds(out min, out max); min -= offset; max -= offset; }
    public IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default) =>
        PointSelectionQuery.Resolve(this, points, cancellationToken);
}
