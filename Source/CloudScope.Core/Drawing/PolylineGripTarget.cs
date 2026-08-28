using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Drawing;

/// <summary>AutoCAD-style vertex and segment-midpoint grips for a 3D polyline.</summary>
public sealed class PolylineGripTarget : ITransactionalGripTarget
{
    private readonly List<GripDescriptor> _grips = [];
    private readonly List<ObjectSnapPoint> _snapPoints = [];
    private Polyline3D _dragStart;
    private GripDragContext _dragContext;
    private int _activeHandle = -1;

    public PolylineGripTarget(Polyline3D polyline)
    {
        Polyline = polyline.Copy();
        _dragStart = Polyline.Copy();
        RebuildDescriptors();
    }

    public event Action<Polyline3D>? Changed;
    public string EditKey => $"POLYLINE:{Polyline.Id}";
    public Polyline3D Polyline { get; private set; }
    public string EditDescription => "Polyline grip edit";
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
        Vector3[] vertices = Polyline.Vertices;
        for (int segment = 0; segment < Polyline.SegmentCount; segment++)
        {
            Vector3 a = vertices[segment];
            Vector3 b = vertices[(segment + 1) % vertices.Length];
            var (ax, ay, ab) = camera.WorldToScreen(a);
            var (bx, by, bb) = camera.WorldToScreen(b);
            if (!ab && !bb && GripInteractionMath.SegmentDistance(mouseX, mouseY, ax, ay, bx, by) <= 8f)
                return true;
        }
        return false;
    }

    public int HitTestHandles(int mouseX, int mouseY, OrbitCamera camera, float threshold = 12f)
    {
        int best = -1;
        float bestDistance = threshold;
        foreach (GripDescriptor grip in Grips)
        {
            float distance = GripManipulator3D.PointHitDistance(grip, camera, mouseX, mouseY);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = grip.Index;
        }
        return best;
    }

    public GripDescriptor GetGrip(int handle) => TryGetGrip(handle, out GripDescriptor grip)
        ? grip
        : throw new ArgumentOutOfRangeException(nameof(handle));

    public bool TryGetGrip(int handle, out GripDescriptor grip)
    {
        foreach (GripDescriptor candidate in Grips)
        {
            if (candidate.Index != handle) continue;
            grip = candidate;
            return true;
        }
        grip = default;
        return false;
    }

    public void BeginHandleDrag(int handle, int mouseX, int mouseY, OrbitCamera camera)
    {
        if (!TryGetGrip(handle, out GripDescriptor grip)) return;
        _activeHandle = handle;
        _dragStart = Polyline.Copy();
        _dragContext = new GripDragContext(
            grip, Polyline.Center, mouseX, mouseY, camera.WorldToViewZ(grip.Position));
    }

    public void UpdateHandleDrag(int mouseX, int mouseY, OrbitCamera camera)
    {
        if (_activeHandle < 0) return;
        Vector3 delta = GripManipulator3D.Translation(_dragContext, camera, mouseX, mouseY);
        ApplyDragDelta(delta);
    }

    public void UpdateHandleDragTo(Vector3 worldPoint)
    {
        if (_activeHandle < 0) return;
        ApplyDragDelta(worldPoint - _dragContext.Grip.Position);
    }

    private void ApplyDragDelta(Vector3 delta)
    {
        Vector3[] vertices = (Vector3[])_dragStart.Vertices.Clone();
        int vertexCount = vertices.Length;
        if (_activeHandle < vertexCount)
        {
            vertices[_activeHandle] += delta;
        }
        else
        {
            int segment = _activeHandle - vertexCount;
            if (segment < 0 || segment >= _dragStart.SegmentCount) return;
            vertices[segment] += delta;
            vertices[(segment + 1) % vertexCount] += delta;
        }
        SetPolyline(_dragStart with { Vertices = vertices });
    }

    public void EndHandleDrag()
    {
        _activeHandle = -1;
        _dragStart = Polyline.Copy();
    }

    public void CancelHandleDrag()
    {
        if (_activeHandle < 0) return;
        Polyline3D restore = _dragStart.Copy();
        _activeHandle = -1;
        SetPolyline(restore);
    }

    public object CaptureState() => Polyline.Copy();

    public void RestoreState(object state)
    {
        if (state is not Polyline3D polyline)
            throw new ArgumentException("Expected a Polyline3D state.", nameof(state));
        _activeHandle = -1;
        SetPolyline(polyline.Copy());
    }

    public bool StatesEqual(object first, object second) =>
        first is Polyline3D a && second is Polyline3D b
        && a.Id == b.Id && a.Name == b.Name && a.Closed == b.Closed
        && a.Vertices.AsSpan().SequenceEqual(b.Vertices);

    private void SetPolyline(Polyline3D polyline)
    {
        Polyline = polyline;
        RebuildDescriptors();
        Changed?.Invoke(polyline.Copy());
    }

    private void RebuildDescriptors()
    {
        _grips.Clear();
        _snapPoints.Clear();
        Vector3[] vertices = Polyline.Vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            _grips.Add(new GripDescriptor(i, GripKind.Endpoint, vertices[i], Vector3.Zero,
                GripConstraint.ViewPlane, IsPrimary: true));
            _snapPoints.Add(new ObjectSnapPoint(vertices[i], ObjectSnapKind.Endpoint, i));
        }

        for (int segment = 0; segment < Polyline.SegmentCount; segment++)
        {
            int next = (segment + 1) % vertices.Length;
            Vector3 midpoint = (vertices[segment] + vertices[next]) * 0.5f;
            int handle = vertices.Length + segment;
            _grips.Add(new GripDescriptor(handle, GripKind.Midpoint, midpoint, Vector3.Zero,
                GripConstraint.ViewPlane));
            _snapPoints.Add(new ObjectSnapPoint(midpoint, ObjectSnapKind.Midpoint, handle));
        }
    }
}
