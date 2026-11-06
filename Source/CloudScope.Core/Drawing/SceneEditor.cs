using CloudScope.Commands;
using CloudScope.Measurements;
using CloudScope.Sections;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Drawing;

/// <summary>Document-owned entities, edit targets and stable-identity transactions, independent of a host or renderer.</summary>
public sealed class SceneEditor
{
    private readonly List<PolylineGripTarget> _polylines = [];
    private readonly List<PlanarPolylineGripTarget> _planar = [];
    private readonly List<ITransactionalGripTarget> _targets = [];
    public IReadOnlyList<PolylineGripTarget> Polylines => _polylines;
    public IReadOnlyList<PlanarPolylineGripTarget> PlanarPolylines => _planar;
    public IReadOnlyList<ITransactionalGripTarget> Targets => _targets;
    public SectionCatalog Sections { get; } = new();
    public MeasurementCatalog Measurements { get; } = new();
    public UndoManager History { get; } = new();
    public int NextPolylineId { get; private set; } = 1;
    public event Action<ITransactionalGripTarget>? TargetRemoved;

    public int AllocatePolylineId() => checked(NextPolylineId++);
    public void Register(ITransactionalGripTarget target) { if (!_targets.Contains(target)) _targets.Add(target); }
    public void Unregister(ITransactionalGripTarget? target)
    {
        if (target != null && _targets.Remove(target)) TargetRemoved?.Invoke(target);
    }
    public PolylineGripTarget Add(Polyline3D polyline)
    {
        RemovePolyline(polyline.Id);
        var target = new PolylineGripTarget(polyline); _polylines.Add(target); Register(target);
        NextPolylineId = Math.Max(NextPolylineId, checked(polyline.Id + 1)); return target;
    }
    public PlanarPolylineGripTarget Add(PlanarPolyline polyline)
    {
        RemovePlanar(polyline.Id);
        var target = new PlanarPolylineGripTarget(polyline); _planar.Add(target); Register(target);
        NextPolylineId = Math.Max(NextPolylineId, checked(polyline.Id + 1)); return target;
    }
    public void RemovePolyline(int id)
    {
        var target = _polylines.FirstOrDefault(t => t.Polyline.Id == id);
        if (target != null) { Unregister(target); _polylines.Remove(target); }
    }
    public void RemovePlanar(int id)
    {
        var target = _planar.FirstOrDefault(t => t.Polyline.Id == id);
        if (target != null) { Unregister(target); _planar.Remove(target); }
    }
    public void RecordCreation(Polyline3D line) => History.Record(new DelegateUndoAction("3D polyline",
        () => RemovePolyline(line.Id), () => Add(line), 256 + line.Vertices.LongLength * 12));
    public void RecordCreation(PlanarPolyline line) => History.Record(new DelegateUndoAction("Polyline",
        () => RemovePlanar(line.Id), () => Add(line), 256 + line.Vertices.LongLength * 20));

    public void RecordEdit(string key, string description, object before, object after) =>
        History.Record(new DelegateUndoAction(description, () => Restore(key, before), () => Restore(key, after), Estimate(before) + Estimate(after)));
    public void Restore(string key, object state) => _targets.FirstOrDefault(t => t.EditKey == key)?.RestoreState(state);
    private static long Estimate(object state) => state switch
    { PlanarPolyline line => 256 + line.Vertices.LongLength * 20, Polyline3D line => 256 + line.Vertices.LongLength * 12, _ => 256 };

    public void ReplaceMeasurements(Measurement[] next)
    {
        var before = Measurements.Items.ToArray(); Measurements.Replace(next);
        History.Record(new DelegateUndoAction("Measurement", () => Measurements.Replace(before), () => Measurements.Replace(next)));
    }
    public PlanarPolyline[] Import(IEnumerable<PlanarPolyline> lines)
    {
        var loaded = lines.Select(line =>
        {
            int id = AllocatePolylineId(); return line with { Id = id, Name = $"PL{id}" };
        }).ToArray();
        foreach (var line in loaded) Add(line);
        History.Record(new DelegateUndoAction("Load polylines",
            () => { foreach (var line in loaded) RemovePlanar(line.Id); },
            () => { foreach (var line in loaded) Add(line); }, 256 + loaded.Sum(line => line.Vertices.LongLength * 20)));
        return loaded;
    }
    public void Edit(PlanarPolylineGripTarget target, string description, Action<PlanarPolylineGripTarget> edit)
    {
        var before = target.Polyline.Copy(); edit(target); var after = target.Polyline.Copy();
        if (!target.StatesEqual(before, after)) RecordEdit(target.EditKey, description, before, after);
    }
    public string Join(PlanarPolylineGripTarget selected, float tolerance)
    {
        if (selected.Polyline.Closed)
            return "A closed polyline cannot be joined at an endpoint.";

        tolerance = MathF.Max(tolerance, 0f);
        PlanarPolyline a = selected.Polyline.Copy();
        PlanarPolylineGripTarget? otherTarget = null;
        PlanarPolyline? joined = null;
        foreach (PlanarPolylineGripTarget candidate in _planar)
        {
            if (ReferenceEquals(candidate, selected) || candidate.Polyline.Closed) continue;
            if (MathF.Abs(Vector3.Dot(candidate.Polyline.Origin - a.Origin, a.Normal)) > tolerance)
                continue;

            PlanarPolyline b = candidate.Polyline.Copy();
            if (PlanarPolylineOperations.TryJoin(a, b, tolerance, out joined))
            {
                otherTarget = candidate;
                break;
            }
        }
        if (otherTarget == null || joined == null)
            return "No open coplanar polyline endpoint is within the join tolerance.";

        PlanarPolyline beforeSelected = selected.Polyline.Copy();
        PlanarPolyline removed = otherTarget.Polyline.Copy();
        selected.RestoreState(joined);
        RemovePlanar(removed.Id);
        History.Record(new DelegateUndoAction(
            "Join polylines",
            () =>
            {
                Restore(selected.EditKey, beforeSelected);
                Add(removed);
            },
            () =>
            {
                Restore(selected.EditKey, joined);
                RemovePlanar(removed.Id);
            }, 768 + (beforeSelected.Vertices.LongLength + removed.Vertices.LongLength + joined.Vertices.LongLength) * 20));
        return $"Polyline {removed.Name} joined to {selected.Polyline.Name}.";
    }

    public void Clear()
    {
        foreach (var target in _targets.ToArray()) Unregister(target);
        _polylines.Clear(); _planar.Clear(); Sections.Replace([], null); Measurements.Replace([]); NextPolylineId = 1;
    }
}
