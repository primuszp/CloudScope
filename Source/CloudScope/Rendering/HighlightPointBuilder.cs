using CloudScope.Labeling;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

internal static class HighlightPointBuilder
{
    public static int FillPreview(
        PointData[] points,
        IReadOnlyList<int> indices,
        Span<PointData> destination)
    {
        int count = 0;
        foreach (int pointIndex in indices)
        {
            if ((uint)pointIndex >= (uint)points.Length)
                continue;

            PointData point = points[pointIndex];
            point.R = 1.0f;
            point.G = 0.85f;
            point.B = 0.1f;
            destination[count++] = point;
        }

        return count;
    }

    public static int FillAnnotations(
        PointData[] points,
        IReadOnlyDictionary<int, PointAnnotation> annotations,
        Func<PointAnnotation, Vector3> annotationColor,
        Span<PointData> destination)
    {
        int count = 0;
        foreach (var (pointIndex, annotation) in annotations)
        {
            if ((uint)pointIndex >= (uint)points.Length)
                continue;

            PointData point = points[pointIndex];
            Vector3 color = annotationColor(annotation);
            point.R = color.X;
            point.G = color.Y;
            point.B = color.Z;
            destination[count++] = point;
        }

        return count;
    }
}
