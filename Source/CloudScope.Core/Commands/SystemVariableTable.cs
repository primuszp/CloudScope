using System.Globalization;

namespace CloudScope.Commands;

public enum SystemVariableType { Bool, Int, Real, String, Enum }

/// <summary>
/// One named piece of viewer state, readable and usually writable from the command line.
/// </summary>
/// <remarks>
/// A variable stores nothing itself: it is a reader and a writer pointing at the field that
/// already exists. That is deliberate — a variable that kept its own copy would be a second
/// source of truth, which is the problem the table was introduced to remove.
/// </remarks>
public sealed class SystemVariable
{
    public required string Name { get; init; }
    public required SystemVariableType Type { get; init; }
    public required string Description { get; init; }

    /// <summary>Accepted values for an enum variable; empty otherwise.</summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    public required Func<string> Getter { get; init; }

    /// <summary>Applies a new value and returns the message to report, or null when read-only.</summary>
    public Func<string, string>? Setter { get; init; }

    public bool IsReadOnly => Setter == null;

    public string Value => Getter();

    public string ListLine =>
        $"  {Name,-16} {Value,-14} {(IsReadOnly ? "(read-only) " : "")}{Description}";
}

/// <summary>
/// The single place viewer state is named. SETVAR and GETVAR read it, and commands that set
/// state write through it, so the status line, the menu check marks and the command line
/// cannot disagree about what the viewer is currently doing.
/// </summary>
public sealed class SystemVariableTable
{
    private readonly Dictionary<string, SystemVariable> _variables = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<SystemVariable> All => _variables.Values;

    public bool TryGet(string name, out SystemVariable variable) => _variables.TryGetValue(name, out variable!);

    public void Register(SystemVariable variable)
    {
        if (!_variables.TryAdd(variable.Name, variable))
            throw new InvalidOperationException($"System variable '{variable.Name}' is already registered.");
    }

    public void RegisterBool(string name, string description, Func<bool> get, Action<bool>? set = null) =>
        Register(new SystemVariable
        {
            Name = name,
            Type = SystemVariableType.Bool,
            Description = description,
            AllowedValues = ["0", "1"],
            Getter = () => get() ? "1" : "0",
            Setter = set == null
                ? null
                : text =>
                {
                    if (!TryParseBool(text, out bool value))
                        return $"{name} takes 0 or 1.";

                    set(value);
                    return $"{name} = {(get() ? "1" : "0")}";
                }
        });

    public void RegisterInt(string name, string description, Func<int> get, Action<int>? set = null,
        int min = int.MinValue, int max = int.MaxValue) =>
        Register(new SystemVariable
        {
            Name = name,
            Type = SystemVariableType.Int,
            Description = description,
            Getter = () => get().ToString(CultureInfo.InvariantCulture),
            Setter = set == null
                ? null
                : text =>
                {
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                        return $"{name} takes a whole number.";
                    if (value < min || value > max)
                        return $"{name} must be between {min} and {max}.";

                    set(value);
                    return $"{name} = {get().ToString(CultureInfo.InvariantCulture)}";
                }
        });

    public void RegisterReal(string name, string description, Func<double> get, Action<double>? set = null,
        double min = double.NegativeInfinity, double max = double.PositiveInfinity) =>
        Register(new SystemVariable
        {
            Name = name,
            Type = SystemVariableType.Real,
            Description = description,
            Getter = () => get().ToString("0.###", CultureInfo.InvariantCulture),
            Setter = set == null
                ? null
                : text =>
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        return $"{name} takes a number.";
                    if (value < min || value > max)
                        return $"{name} must be between {min.ToString("0.###", CultureInfo.InvariantCulture)} "
                             + $"and {max.ToString("0.###", CultureInfo.InvariantCulture)}.";

                    set(value);
                    return $"{name} = {get().ToString("0.###", CultureInfo.InvariantCulture)}";
                }
        });

    public void RegisterString(string name, string description, Func<string> get, Action<string>? set = null) =>
        Register(new SystemVariable
        {
            Name = name,
            Type = SystemVariableType.String,
            Description = description,
            Getter = get,
            Setter = set == null ? null : text => { set(text); return $"{name} = {get()}"; }
        });

    public void RegisterEnum(string name, string description, IReadOnlyList<string> allowed,
        Func<string> get, Action<string>? set = null) =>
        Register(new SystemVariable
        {
            Name = name,
            Type = SystemVariableType.Enum,
            Description = description,
            AllowedValues = allowed,
            Getter = get,
            Setter = set == null
                ? null
                : text =>
                {
                    string? match = allowed.FirstOrDefault(v => v.Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        return $"{name} takes one of: {string.Join(", ", allowed)}.";

                    set(match);
                    return $"{name} = {get()}";
                }
        });

    /// <summary>Reads a variable, or reports that it is unknown.</summary>
    public string Read(string name) =>
        _variables.TryGetValue(name, out SystemVariable? variable)
            ? $"{variable.Name} = {variable.Value}"
            : $"Unknown system variable: {name}";

    /// <summary>Writes a variable and returns the message to report.</summary>
    public string Write(string name, string value)
    {
        if (!_variables.TryGetValue(name, out SystemVariable? variable))
            return $"Unknown system variable: {name}";

        if (variable.Setter == null)
            return $"{variable.Name} is read-only (currently {variable.Value}).";

        return variable.Setter(value);
    }

    /// <summary>Variables whose name contains <paramref name="pattern"/>; all of them when it is empty.</summary>
    public IEnumerable<SystemVariable> Match(string pattern) =>
        _variables.Values
            .Where(v => pattern.Length == 0 || pattern == "*" ||
                        v.Name.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);

    private static bool TryParseBool(string text, out bool value)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "1": case "ON": case "TRUE": case "YES": value = true; return true;
            case "0": case "OFF": case "FALSE": case "NO": value = false; return true;
            default: value = false; return false;
        }
    }
}
