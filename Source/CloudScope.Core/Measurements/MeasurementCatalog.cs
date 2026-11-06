namespace CloudScope.Measurements;

/// <summary>Validated document measurements; callers replace snapshots for undo and project restore.</summary>
public sealed class MeasurementCatalog
{
    private readonly List<Measurement> _items = [];
    private readonly Dictionary<int, MeasurementResult> _results = [];
    public IReadOnlyList<Measurement> Items => _items.AsReadOnly();
    public MeasurementResult Result(int id) => _results[id];
    public int NextId => _items.Count == 0 ? 1 : checked(_items.Max(m => m.Id) + 1);
    public void Replace(IEnumerable<Measurement> measurements)
    {
        var next = measurements.ToArray();
        var results = next.Select(m => m.Evaluate()).ToArray();
        if (next.Select(m => m.Id).Distinct().Count() != next.Length
            || next.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != next.Length)
            throw new InvalidDataException("Measurement names and identities must be unique.");
        _items.Clear(); _items.AddRange(next); _results.Clear();
        for (int i = 0; i < next.Length; i++) _results.Add(next[i].Id, results[i]);
    }
}
