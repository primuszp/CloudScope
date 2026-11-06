using OpenTK.Mathematics;

namespace CloudScope.Sections;

/// <summary>Owns named sections and their active identity independently of viewport adapters.</summary>
public sealed class SectionCatalog
{
    private readonly List<SectionDefinition> _items = [];
    public IReadOnlyList<SectionDefinition> Items => _items.AsReadOnly();
    public int? ActiveId { get; private set; }
    public SectionDefinition? Active => _items.FirstOrDefault(s => s.Id == ActiveId) is { Id: > 0 } value ? value : null;
    public int NextId => _items.Count == 0 ? 1 : checked(_items.Max(s => s.Id) + 1);

    public static void Validate(SectionDefinition section)
    {
        static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
        if (section.Id <= 0 || section.Id == int.MaxValue || string.IsNullOrWhiteSpace(section.Name)
            || !Finite(section.Start) || !Finite(section.End) || !float.IsFinite(section.Length) || section.Length < .001f
            || !float.IsFinite(section.Width) || section.Width < .001f)
            throw new InvalidDataException("Invalid section geometry or identity.");
    }

    public void SetActive(SectionDefinition? section)
    {
        if (section is not { } value) { ActiveId = null; return; }
        Validate(value);
        if (_items.Any(s => s.Id != value.Id && string.Equals(s.Name, value.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A section with this name already exists.");
        int index = _items.FindIndex(s => s.Id == value.Id);
        if (index < 0) _items.Add(value); else _items[index] = value;
        ActiveId = value.Id;
    }

    public bool Activate(string name)
    {
        var section = _items.Find(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (section.Id == 0) return false;
        ActiveId = section.Id; return true;
    }

    public void RemoveActive()
    {
        _items.RemoveAll(s => s.Id == ActiveId);
        ActiveId = _items.Count == 0 ? null : _items[^1].Id;
    }

    public void Replace(IEnumerable<SectionDefinition> sections, int? activeId)
    {
        var next = sections.ToArray();
        foreach (var section in next) Validate(section);
        if (next.Select(s => s.Id).Distinct().Count() != next.Length
            || next.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != next.Length
            || activeId.HasValue && !next.Any(s => s.Id == activeId))
            throw new InvalidDataException("Invalid section collection.");
        _items.Clear(); _items.AddRange(next); ActiveId = activeId;
    }
}
