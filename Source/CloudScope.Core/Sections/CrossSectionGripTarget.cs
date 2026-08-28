using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Sections;

/// <summary>Grip-editable adapter around an immutable <see cref="SectionDefinition"/>.</summary>
public sealed class CrossSectionGripTarget : IGripTarget
{
    public const int StartGrip = 0;
    public const int EndGrip = 1;
    public const int CenterGrip = 2;
    public const int PositiveWidthGrip = 3;
    public const int NegativeWidthGrip = 4;
    public const int DirectionGrip = 5;
    private const float MinimumSize = 0.001f;

    private readonly List<GripDescriptor> _grips = new(6);
    private SectionDefinition _dragStart;
    private GripDragContext _dragContext;
    private int _activeHandle = -1;

    public CrossSectionGripTarget(SectionDefinition section) => SetSection(section);

    public event Action<SectionDefinition>? Changed;
    public SectionDefinition Section { get; private set; }
    public IReadOnlyList<GripDescriptor> Grips { get { RebuildGrips(); return _grips; } }
    public int CenterGripIndex => CenterGrip;
    public int HoveredHandle { get; set; } = -1;
    public int ActiveHandle => _activeHandle;
    public bool IsHandleDragging => _activeHandle >= 0;

    public void SetSection(SectionDefinition section)
    {
        Section = section;
        if (!IsHandleDragging) _dragStart = section;
        RebuildGrips();
    }

    public bool IsGripVisible(int index) => index is >= StartGrip and <= DirectionGrip;

    public bool HitTestBody(int mouseX, int mouseY, OrbitCamera camera)
    {
        SectionDefinition section = Section;
        Vector3 n = section.Normal * (section.Width * 0.5f);
        return SegmentDistance(camera, section.Start, section.End, mouseX, mouseY) <= 8f
            || SegmentDistance(camera, section.Start + n, section.End + n, mouseX, mouseY) <= 8f
            || SegmentDistance(camera, section.Start - n, section.End - n, mouseX, mouseY) <= 8f;
    }

    public int HitTestHandles(int mouseX, int mouseY, OrbitCamera camera, float threshold = 12f)
    {
        int best = -1;
        float bestDistance = threshold;
        foreach (GripDescriptor grip in Grips)
        {
            float distance = GripManipulator3D.PointHitDistance(grip, camera, mouseX, mouseY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = grip.Index;
            }
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
        _dragStart = Section;
        _dragContext = new GripDragContext(
            grip, Section.Center, mouseX, mouseY, camera.WorldToViewZ(grip.Position));
    }

    public void UpdateHandleDrag(int mouseX, int mouseY, OrbitCamera camera)
    {
        if (_activeHandle < 0) return;
        Vector3 delta = GripManipulator3D.Translation(_dragContext, camera, mouseX, mouseY);
        delta.Z = 0f;
        SectionDefinition next = _dragStart;

        switch (_activeHandle)
        {
            case StartGrip:
                Vector3 start = _dragStart.Start + delta;
                if (HorizontalDistance(start, _dragStart.End) >= MinimumSize)
                    next = _dragStart with { Start = start };
                break;
            case EndGrip:
                Vector3 end = _dragStart.End + delta;
                if (HorizontalDistance(_dragStart.Start, end) >= MinimumSize)
                    next = _dragStart with { End = end };
                break;
            case CenterGrip:
                next = _dragStart with { Start = _dragStart.Start + delta, End = _dragStart.End + delta };
                break;
            case PositiveWidthGrip:
            case NegativeWidthGrip:
                Vector3 world = camera.ScreenToWorldAtDepth(mouseX, mouseY, _dragContext.ViewZ);
                float halfWidth = MathF.Abs(Vector3.Dot(world - _dragStart.Center, _dragStart.Normal));
                next = _dragStart with { Width = MathF.Max(halfWidth * 2f, MinimumSize) };
                break;
            case DirectionGrip:
                Vector3 point = camera.ScreenToWorldAtDepth(mouseX, mouseY, _dragContext.ViewZ);
                Vector3 along = _dragStart.Along;
                Vector3 positiveNormal = new(-along.Y, along.X, 0f);
                next = _dragStart with { Flipped = Vector3.Dot(point - _dragStart.Center, positiveNormal) < 0f };
                break;
        }

        if (next == Section) return;
        Section = next;
        RebuildGrips();
        Changed?.Invoke(next);
    }

    public void EndHandleDrag()
    {
        _activeHandle = -1;
        _dragStart = Section;
    }

    public void CancelHandleDrag()
    {
        if (_activeHandle < 0) return;
        Section = _dragStart;
        _activeHandle = -1;
        RebuildGrips();
        Changed?.Invoke(Section);
    }

    private void RebuildGrips()
    {
        _grips.Clear();
        Vector3 normal = Section.Normal;
        float halfWidth = Section.Width * 0.5f;
        float arrowLength = MathF.Max(Section.Width, Section.Length * 0.08f);
        _grips.Add(new(StartGrip, GripKind.Endpoint, Section.Start, Vector3.Zero, GripConstraint.ViewPlane, IsPrimary: true));
        _grips.Add(new(EndGrip, GripKind.Endpoint, Section.End, Vector3.Zero, GripConstraint.ViewPlane, IsPrimary: true));
        _grips.Add(GripDescriptor.Center(CenterGrip, Section.Center));
        _grips.Add(new(PositiveWidthGrip, GripKind.WidthResize, Section.Center + normal * halfWidth,
            normal, GripConstraint.Axis, Sign: 1));
        _grips.Add(new(NegativeWidthGrip, GripKind.WidthResize, Section.Center - normal * halfWidth,
            -normal, GripConstraint.Axis, Sign: -1));
        _grips.Add(new(DirectionGrip, GripKind.Direction, Section.Center + normal * arrowLength,
            normal, GripConstraint.Axis));
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        new Vector2(a.X - b.X, a.Y - b.Y).Length;

    private static float SegmentDistance(OrbitCamera camera, Vector3 a, Vector3 b, int x, int y)
    {
        var (ax, ay, aBehind) = camera.WorldToScreen(a);
        var (bx, by, bBehind) = camera.WorldToScreen(b);
        return aBehind || bBehind ? float.MaxValue : GripInteractionMath.SegmentDistance(x, y, ax, ay, bx, by);
    }
}
