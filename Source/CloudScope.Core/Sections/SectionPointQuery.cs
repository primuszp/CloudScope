using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Sections;

/// <summary>The full finite section slab, including its boundary, without a render LOD or point budget.</summary>
public sealed class SectionPointQuery(SectionDefinition section) : IPointSelectionQuery
{
    private readonly SectionClip _clip = section.ToClip();
    public bool IsEmpty => false;
    public bool Contains(float x, float y, float z) => _clip.Contains(new(x,y,z));
    public void GetBounds(out Vector3 min, out Vector3 max)
    {
        Vector3 extent = Vector3.ComponentMax(_clip.Along * _clip.HalfLength, -_clip.Along * _clip.HalfLength)
            + Vector3.ComponentMax(_clip.Normal * _clip.HalfWidth, -_clip.Normal * _clip.HalfWidth);
        min = _clip.Center - extent; max = _clip.Center + extent;
        min.Z = -float.MaxValue; max.Z = float.MaxValue;
    }
    public IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default) =>
        PointSelectionQuery.Resolve(this,points,cancellationToken);
}
