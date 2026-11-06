namespace CloudScope.Measurements;

/// <summary>Validated document measurements; callers replace snapshots for undo and project restore.</summary>
public sealed class MeasurementCatalog
{
    private readonly List<Measurement> _items = [];
    public IReadOnlyList<Measurement> Items => _items.AsReadOnly();
    public int NextId => _items.Count == 0 ? 1 : checked(_items.Max(m => m.Id) + 1);
    public void Replace(IEnumerable<Measurement> measurements)
    {
        var next = measurements.ToArray();
        foreach (var measurement in next) measurement.Evaluate();
        if (next.Select(m => m.Id).Distinct().Count() != next.Length
            || next.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != next.Length)
            throw new InvalidDataException("Measurement names and identities must be unique.");
        _items.Clear(); _items.AddRange(next);
    }
}
