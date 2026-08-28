using OpenTK.Mathematics;

namespace CloudScope.Selection;

public enum ObjectSnapKind
{
    None,
    Endpoint,
    Midpoint,
    Center,
    Grip,
    AxisX,
    AxisY,
    AxisZ
}

public readonly record struct ObjectSnapPoint(
    Vector3 Position,
    ObjectSnapKind Kind,
    int SourceIndex = -1);

public interface IObjectSnapSource
{
    IReadOnlyList<ObjectSnapPoint> SnapPoints { get; }
}

/// <summary>View of a snap source with one actively dragged grip removed.</summary>
public sealed class FilteredObjectSnapSource : IObjectSnapSource
{
    private readonly IObjectSnapSource _source;
    private readonly int _excludedSourceIndex;
    private readonly List<ObjectSnapPoint> _points = [];

    public FilteredObjectSnapSource(IObjectSnapSource source, int excludedSourceIndex)
    {
        _source = source;
        _excludedSourceIndex = excludedSourceIndex;
    }

    public IReadOnlyList<ObjectSnapPoint> SnapPoints
    {
        get
        {
            _points.Clear();
            foreach (ObjectSnapPoint point in _source.SnapPoints)
                if (point.SourceIndex != _excludedSourceIndex)
                    _points.Add(point);
            return _points;
        }
    }
}

public readonly record struct ObjectSnapResult(
    Vector3 Position,
    ObjectSnapKind Kind,
    Vector3? GuideStart = null,
    Vector3? GuideEnd = null)
{
    public bool IsSnapped => Kind != ObjectSnapKind.None;
    public bool IsAxis => Kind is ObjectSnapKind.AxisX or ObjectSnapKind.AxisY or ObjectSnapKind.AxisZ;
}

/// <summary>Screen-space object snap and automatic world-axis tracking.</summary>
public sealed class ObjectSnapEngine
{
    public float ObjectThresholdPixels { get; set; } = 14f;
    public float AxisThresholdPixels { get; set; } = 10f;

    public ObjectSnapResult Resolve(
        Vector3 rawPoint,
        int mouseX,
        int mouseY,
        OrbitCamera camera,
        IEnumerable<IObjectSnapSource> sources,
        Vector3? basePoint)
    {
        ObjectSnapResult objectResult = FindObjectSnap(mouseX, mouseY, camera, sources);
        if (objectResult.IsSnapped)
            return objectResult;

        if (basePoint is { } origin)
        {
            ObjectSnapResult axisResult = FindAxisSnap(mouseX, mouseY, camera, origin);
            if (axisResult.IsSnapped)
                return axisResult;
        }

        return new ObjectSnapResult(rawPoint, ObjectSnapKind.None);
    }

    private ObjectSnapResult FindObjectSnap(
        int mouseX,
        int mouseY,
        OrbitCamera camera,
        IEnumerable<IObjectSnapSource> sources)
    {
        float bestDistance = ObjectThresholdPixels;
        ObjectSnapPoint best = default;
        bool found = false;
        foreach (IObjectSnapSource source in sources)
        {
            foreach (ObjectSnapPoint point in source.SnapPoints)
            {
                float distance = GripManipulator3D.PointHitDistance(
                    new GripDescriptor(0, GripKind.Endpoint, point.Position, Vector3.Zero, GripConstraint.ViewPlane),
                    camera, mouseX, mouseY);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = point;
                found = true;
            }
        }

        return found
            ? new ObjectSnapResult(best.Position, best.Kind)
            : default;
    }

    private ObjectSnapResult FindAxisSnap(int mouseX, int mouseY, OrbitCamera camera, Vector3 origin)
    {
        Vector3[] axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        ObjectSnapKind[] kinds = [ObjectSnapKind.AxisX, ObjectSnapKind.AxisY, ObjectSnapKind.AxisZ];
        var (originX, originY, originBehind) = camera.WorldToScreen(origin);
        if (originBehind) return default;

        float scale = MathF.Max(camera.WorldUnitsPerPixel(origin) * 100f, 0.01f);
        float originViewZ = camera.WorldToViewZ(origin);
        Vector3 rayStart = camera.ScreenToWorldAtDepth(mouseX, mouseY, originViewZ);
        Vector3 rayEnd = camera.ScreenToWorldAtDepth(mouseX, mouseY, originViewZ - MathF.Max(scale, 1f));
        Vector3 rayDirection = rayEnd - rayStart;
        if (rayDirection.LengthSquared < 1e-10f) return default;
        rayDirection.Normalize();
        float bestDistance = AxisThresholdPixels;
        ObjectSnapResult best = default;
        for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
        {
            Vector3 axis = axes[axisIndex];
            var (axisX, axisY, behind) = camera.WorldToScreen(origin + axis * scale);
            if (behind) continue;
            float screenDx = axisX - originX;
            float screenDy = axisY - originY;
            if (screenDx * screenDx + screenDy * screenDy < 1f) continue;

            Vector3 betweenOrigins = origin - rayStart;
            float parallel = Vector3.Dot(axis, rayDirection);
            float denominator = 1f - parallel * parallel;
            if (MathF.Abs(denominator) < 1e-5f) continue;
            float axisDistance = (parallel * Vector3.Dot(rayDirection, betweenOrigins)
                - Vector3.Dot(axis, betweenOrigins)) / denominator;
            Vector3 snapped = origin + axis * axisDistance;
            var (projectedX, projectedY, projectedBehind) = camera.WorldToScreen(snapped);
            if (projectedBehind) continue;
            float distance = MathF.Sqrt(
                (mouseX - projectedX) * (mouseX - projectedX)
                + (mouseY - projectedY) * (mouseY - projectedY));
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = new ObjectSnapResult(snapped, kinds[axisIndex], origin, snapped);
        }
        return best;
    }
}
