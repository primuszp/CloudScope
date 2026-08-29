using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Drawing;

/// <summary>Vertex and segment grips for a planar line/arc polyline.</summary>
public sealed class PlanarPolylineGripTarget : ITransactionalGripTarget
{
    private readonly List<GripDescriptor> _grips = [];
    private readonly List<ObjectSnapPoint> _snapPoints = [];
    private PlanarPolyline _dragStart;
    private GripDragContext _dragContext;
    private int _activeHandle = -1;

    public PlanarPolylineGripTarget(PlanarPolyline polyline)
    {
        Polyline = polyline.Copy();
        _dragStart = Polyline.Copy();
        RebuildDescriptors();
    }

    public event Action<PlanarPolyline>? Changed;
    public PlanarPolyline Polyline { get; private set; }
    public string EditKey => $"PLINE:{Polyline.Id}";
    public string EditDescription => "Planar polyline grip edit";
    public Vector3 DragAnchor => _dragContext.Grip.Position;
    public IReadOnlyList<GripDescriptor> Grips => _grips;
    public IReadOnlyList<ObjectSnapPoint> SnapPoints => _snapPoints;
    public int CenterGripIndex => -1;
    public int HoveredHandle { get; set; } = -1;
    public int ActiveHandle => _activeHandle;
    public bool IsHandleDragging => _activeHandle >= 0;
    public bool IsGripVisible(int index) => index >= 0 && index < _grips.Count;

    public bool HitTestBody(int mouseX, int mouseY, OrbitCamera camera)
    {
        Vector3[] points = PlanarPolylineGeometry.Tessellate(Polyline);
        for (int i = 0; i + 1 < points.Length; i++)
        {
            var (ax, ay, ab) = camera.WorldToScreen(points[i]);
            var (bx, by, bb) = camera.WorldToScreen(points[i + 1]);
            if (!ab && !bb && GripInteractionMath.SegmentDistance(mouseX, mouseY, ax, ay, bx, by) <= 8f)
                return true;
        }
        return false;
    }

    public int HitTestHandles(int mouseX, int mouseY, OrbitCamera camera, float threshold = 12f)
    {
        int best = -1;
        float bestDistance = threshold;
        foreach (GripDescriptor grip in _grips)
        {
            float distance = GripManipulator3D.PointHitDistance(grip, camera, mouseX, mouseY);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = grip.Index;
        }
        return best;
    }

    public GripDescriptor GetGrip(int handle) => TryGetGrip(handle, out GripDescriptor grip)
        ? grip : throw new ArgumentOutOfRangeException(nameof(handle));

    public bool TryGetGrip(int handle, out GripDescriptor grip)
    {
        grip = _grips.FirstOrDefault(item => item.Index == handle);
        return _grips.Any(item => item.Index == handle);
    }

    public void BeginHandleDrag(int handle, int mouseX, int mouseY, OrbitCamera camera)
    {
        if (!TryGetGrip(handle, out GripDescriptor grip)) return;
        _activeHandle = handle;
        _dragStart = Polyline.Copy();
        _dragContext = new GripDragContext(grip, Polyline.Origin, mouseX, mouseY,
            camera.WorldToViewZ(grip.Position));
    }

    public void UpdateHandleDrag(int mouseX, int mouseY, OrbitCamera camera)
    {
        if (_activeHandle < 0) return;
        UpdateHandleDragTo(_dragContext.Grip.Position
            + GripManipulator3D.Translation(_dragContext, camera, mouseX, mouseY));
    }

    public void UpdateHandleDragTo(Vector3 worldPoint)
    {
        if (_activeHandle < 0) return;
        Vector2 delta = _dragStart.ToPlane(worldPoint) - _dragStart.ToPlane(_dragContext.Grip.Position);
        PlanarPolylineVertex[] vertices = (PlanarPolylineVertex[])_dragStart.Vertices.Clone();
        if (_activeHandle < vertices.Length)
        {
            vertices[_activeHandle] = vertices[_activeHandle] with
            {
                Position = vertices[_activeHandle].Position + delta
            };
        }
        else
        {
            int segment = _activeHandle - vertices.Length;
            if (segment < 0 || segment >= _dragStart.SegmentCount) return;
            int next = (segment + 1) % vertices.Length;
            vertices[segment] = vertices[segment] with { Position = vertices[segment].Position + delta };
            vertices[next] = vertices[next] with { Position = vertices[next].Position + delta };
        }
        SetPolyline(_dragStart with { Vertices = vertices });
    }

    public void EndHandleDrag() { _activeHandle = -1; _dragStart = Polyline.Copy(); }
    public void CancelHandleDrag()
    {
        if (_activeHandle < 0) return;
        PlanarPolyline restore = _dragStart.Copy();
        _activeHandle = -1;
        SetPolyline(restore);
    }

    public object CaptureState() => Polyline.Copy();
    public void RestoreState(object state)
    {
        if (state is not PlanarPolyline polyline)
            throw new ArgumentException("Expected a PlanarPolyline state.", nameof(state));
        _activeHandle = -1;
        SetPolyline(polyline.Copy());
    }

    public bool StatesEqual(object first, object second) =>
        first is PlanarPolyline a && second is PlanarPolyline b
        && a.Id == b.Id && a.Name == b.Name && a.Closed == b.Closed
        && a.Origin == b.Origin && a.AxisX == b.AxisX && a.AxisY == b.AxisY
        && a.Vertices.AsSpan().SequenceEqual(b.Vertices);

    public void SetClosed(bool closed) => SetPolyline(Polyline with { Closed = closed });

    public void SetUniformWidth(float width)
    {
        float value = MathF.Max(width, 0f);
        PlanarPolylineVertex[] vertices = (PlanarPolylineVertex[])Polyline.Vertices.Clone();
        for (int i = 0; i < Polyline.SegmentCount; i++)
            vertices[i] = vertices[i] with { StartWidth = value, EndWidth = value };
        SetPolyline(Polyline with { Vertices = vertices });
    }

    public void Reverse()
    {
        PlanarPolylineVertex[] old = Polyline.Vertices;
        var reversed = new PlanarPolylineVertex[old.Length];
        for (int i = 0; i < old.Length; i++)
        {
            int sourceVertex = old.Length - 1 - i;
            int incoming = (sourceVertex - 1 + old.Length) % old.Length;
            PlanarPolylineVertex source = old[sourceVertex];
            PlanarPolylineVertex sourceSegment = old[incoming];
            reversed[i] = source with
            {
                Bulge = -sourceSegment.Bulge,
                StartWidth = sourceSegment.EndWidth,
                EndWidth = sourceSegment.StartWidth
            };
        }
        if (!Polyline.Closed && reversed.Length > 0)
            reversed[^1] = reversed[^1] with { Bulge = 0f, StartWidth = 0f, EndWidth = 0f };
        SetPolyline(Polyline with { Vertices = reversed });
    }

    private void SetPolyline(PlanarPolyline polyline)
    {
        Polyline = polyline;
        RebuildDescriptors();
        Changed?.Invoke(polyline.Copy());
    }

    private void RebuildDescriptors()
    {
        _grips.Clear();
        _snapPoints.Clear();
        for (int i = 0; i < Polyline.Vertices.Length; i++)
        {
            Vector3 point = Polyline.ToWorld(Polyline.Vertices[i].Position);
            _grips.Add(new GripDescriptor(i, GripKind.Endpoint, point, Vector3.Zero,
                GripConstraint.ViewPlane, IsPrimary: true));
            _snapPoints.Add(new ObjectSnapPoint(point, ObjectSnapKind.Endpoint, i));
        }
        for (int segment = 0; segment < Polyline.SegmentCount; segment++)
        {
            int next = (segment + 1) % Polyline.Vertices.Length;
            PlanarPolylineVertex start = Polyline.Vertices[segment];
            PlanarPolylineVertex end = Polyline.Vertices[next];
            Vector2 midpoint = PlanarPolylineGeometry.SegmentMidpoint(start, end);
            int handle = Polyline.Vertices.Length + segment;
            Vector3 point = Polyline.ToWorld(midpoint);
            _grips.Add(new GripDescriptor(handle, GripKind.Midpoint, point, Vector3.Zero,
                GripConstraint.ViewPlane));
            _snapPoints.Add(new ObjectSnapPoint(point, ObjectSnapKind.Midpoint, handle));
            if (PlanarPolylineGeometry.TryGetArc(start, end, out Vector2 center,
                    out float radius, out float startAngle, out float sweep))
            {
                int snapBase = 100_000 + segment * 10;
                _snapPoints.Add(new ObjectSnapPoint(
                    Polyline.ToWorld(center), ObjectSnapKind.Center, snapBase));
                Vector2[] quadrants =
                [
                    center + Vector2.UnitX * radius,
                    center - Vector2.UnitX * radius,
                    center + Vector2.UnitY * radius,
                    center - Vector2.UnitY * radius
                ];
                for (int quadrant = 0; quadrant < quadrants.Length; quadrant++)
                {
                    Vector2 radial = quadrants[quadrant] - center;
                    float angle = MathF.Atan2(radial.Y, radial.X);
                    if (!PlanarPolylineGeometry.ContainsArcAngle(startAngle, sweep, angle))
                        continue;
                    _snapPoints.Add(new ObjectSnapPoint(
                        Polyline.ToWorld(quadrants[quadrant]), ObjectSnapKind.Quadrant,
                        snapBase + quadrant + 1));
                }
            }
        }
    }
}
