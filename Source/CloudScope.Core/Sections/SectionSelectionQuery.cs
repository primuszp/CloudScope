using System.Collections.Generic;
using System.Threading;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Sections;

/// <summary>Intersects an ordinary selection volume with the points visible in a section.</summary>
public sealed class SectionSelectionQuery(IPointSelectionQuery inner, SectionClip section)
    : IPointSelectionQuery
{
    public bool IsEmpty => inner.IsEmpty;

    public bool Contains(float x, float y, float z) =>
        inner.Contains(x, y, z) && section.Contains(new Vector3(x, y, z));

    public void GetBounds(out Vector3 min, out Vector3 max)
    {
        inner.GetBounds(out min, out max);

        Vector3 along = section.Along * section.HalfLength;
        Vector3 normal = section.Normal * section.HalfWidth;
        Vector3 c0 = section.Center + along + normal;
        Vector3 c1 = section.Center + along - normal;
        Vector3 c2 = section.Center - along + normal;
        Vector3 c3 = section.Center - along - normal;
        float sectionMinX = MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X));
        float sectionMaxX = MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X));
        float sectionMinY = MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y));
        float sectionMaxY = MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y));
        min.X = MathF.Max(min.X, sectionMinX);
        min.Y = MathF.Max(min.Y, sectionMinY);
        max.X = MathF.Min(max.X, sectionMaxX);
        max.Y = MathF.Min(max.Y, sectionMaxY);
    }

    public IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default) =>
        PointSelectionQuery.Resolve(this, points, cancellationToken);
}
