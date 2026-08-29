using System.Globalization;
using OpenTK.Mathematics;

namespace CloudScope.Commands;

/// <summary>A prompt that accepts only its keywords.</summary>
public sealed class PromptKeywordStep(string message) : PromptStep(message, allowArbitraryInput: false)
{
    internal override bool Accept(string input)
    {
        Error = $"Invalid option keyword: {input}";
        return false;
    }
}

/// <summary>A prompt that accepts free-form text.</summary>
public sealed class PromptStringStep(string message) : PromptStep(message, allowArbitraryInput: true)
{
    public string Value { get; private set; } = "";
    private string? _default;

    public PromptStringStep WithDefault(string value)
    {
        _default = value;
        return this;
    }

    internal override bool ApplyDefaultValue()
    {
        if (_default == null) return false;
        Value = _default;
        Status = PromptStatus.OK;
        return true;
    }

    internal override bool Accept(string input)
    {
        Value = CommandText.Unquote(input);
        Status = PromptStatus.OK;
        return true;
    }
}

/// <summary>A prompt that accepts a whole number.</summary>
public sealed class PromptIntegerStep(string message) : PromptStep(message, allowArbitraryInput: true)
{
    public int Value { get; private set; }
    private int? _default;
    private int _min = int.MinValue;
    private int _max = int.MaxValue;

    public PromptIntegerStep WithDefault(int value)
    {
        _default = value;
        return this;
    }

    public PromptIntegerStep WithRange(int min, int max)
    {
        _min = min;
        _max = max;
        return this;
    }

    internal override bool ApplyDefaultValue()
    {
        if (_default == null) return false;
        Value = _default.Value;
        Status = PromptStatus.OK;
        return true;
    }

    internal override bool Accept(string input)
    {
        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            Error = $"Requires an integer value: {input}";
            return false;
        }

        if (parsed < _min || parsed > _max)
        {
            Error = $"Value must be between {_min} and {_max}.";
            return false;
        }

        Value = parsed;
        Status = PromptStatus.OK;
        return true;
    }
}

/// <summary>
/// A prompt that accepts a real number. <c>GetDistance</c> and <c>GetAngle</c> return this
/// type too; they differ only in the range they accept and in how the value is read.
/// </summary>
public sealed class PromptDoubleStep(string message) : PromptStep(message, allowArbitraryInput: true)
{
    public double Value { get; private set; }
    private double? _default;
    private double _min = double.NegativeInfinity;
    private double _max = double.PositiveInfinity;
    private bool _allowRelative;

    /// <summary>Accepts a leading "+" or "-" as an adjustment of <paramref name="current"/>.</summary>
    public PromptDoubleStep AllowingRelative(double current)
    {
        _allowRelative = true;
        Current = current;
        return this;
    }

    public double Current { get; private set; }

    public PromptDoubleStep WithDefault(double value)
    {
        _default = value;
        return this;
    }

    public PromptDoubleStep WithRange(double min, double max)
    {
        _min = min;
        _max = max;
        return this;
    }

    public float Single => (float)Value;

    internal override bool ApplyDefaultValue()
    {
        if (_default == null) return false;
        Value = _default.Value;
        Status = PromptStatus.OK;
        return true;
    }

    internal override bool Accept(string input)
    {
        double parsed;
        if (_allowRelative && (input == "+" || input == "-"))
            parsed = input == "+" ? Current + 0.5 : Current - 0.5;
        else if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            Error = $"Requires a numeric value: {input}";
            return false;
        }

        if (parsed < _min || parsed > _max)
        {
            Error = $"Value must be between {_min.ToString("0.###", CultureInfo.InvariantCulture)} and "
                  + $"{_max.ToString("0.###", CultureInfo.InvariantCulture)}.";
            return false;
        }

        Value = parsed;
        Status = PromptStatus.OK;
        return true;
    }
}

/// <summary>
/// A distance that can either be typed or measured by clicking a world-space point.
/// The supplied measurement function defines the construction geometry (for example,
/// perpendicular distance from a cross-section baseline).
/// </summary>
public sealed class PromptDistanceOrPointStep(
    string message,
    Func<Vector3, double> measure) : PromptStep(message, allowArbitraryInput: true)
{
    private double? _default;
    private double _min;
    private double _max = double.MaxValue;

    public double Value { get; private set; }
    public float Single => (float)Value;

    public PromptDistanceOrPointStep WithDefault(double value)
    {
        _default = value;
        return this;
    }

    public PromptDistanceOrPointStep WithRange(double min, double max)
    {
        _min = min;
        _max = max;
        return this;
    }

    internal override bool ApplyDefaultValue()
    {
        if (_default is not double value) return false;
        Value = value;
        Status = PromptStatus.OK;
        return true;
    }

    internal override bool Accept(string input)
    {
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            Error = $"Requires a distance or viewport pick: {input}";
            return false;
        }

        return AcceptValue(value);
    }

    internal bool AcceptPick(Vector3 world) => AcceptValue(measure(world));

    private bool AcceptValue(double value)
    {
        if (!double.IsFinite(value) || value < _min || value > _max)
        {
            Error = $"Value must be between {_min.ToString("0.###", CultureInfo.InvariantCulture)} and "
                  + $"{_max.ToString("0.###", CultureInfo.InvariantCulture)}.";
            return false;
        }

        Value = value;
        Status = PromptStatus.OK;
        return true;
    }
}

/// <summary>
/// A prompt for a world-space point, typed as "x,y,z" (or "x,y" at <see cref="PlaneZ"/>) or
/// picked in the viewport. The pick is delivered through <see cref="Editor.SupplyPick"/>,
/// which is what makes MOVE, ROTATE and ZOOM Window interactive.
/// </summary>
public sealed class PromptPointStep(string message) : PromptStep(message, allowArbitraryInput: true)
{
    public Vector3 Value { get; internal set; }
    public double PlaneZ { get; private set; }
    private Vector3? _default;
    private Func<double, Vector3>? _directionalDistanceResolver;

    /// <summary>Base point a rubber-banded pick is measured from, when the command has one.</summary>
    public Vector3? BasePoint { get; private set; }

    public PromptPointStep From(Vector3 basePoint)
    {
        BasePoint = basePoint;
        PlaneZ = basePoint.Z;
        return this;
    }

    public PromptPointStep WithDefault(Vector3 value)
    {
        _default = value;
        return this;
    }

    /// <summary>
    /// Accepts one scalar as a distance along the direction currently inferred by the
    /// viewport. Coordinate pairs/triples keep their ordinary meaning.
    /// </summary>
    public PromptPointStep WithDirectionalDistance(Func<double, Vector3> resolver)
    {
        _directionalDistanceResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    internal override bool ApplyDefaultValue()
    {
        if (_default == null) return false;
        Value = _default.Value;
        Status = PromptStatus.OK;
        return true;
    }

    internal override bool Accept(string input)
    {
        if (_directionalDistanceResolver != null
            && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double distance)
            && double.IsFinite(distance))
        {
            Value = _directionalDistanceResolver(distance);
            Status = PromptStatus.OK;
            return true;
        }

        if (!TryParsePoint(input, BasePoint, PlaneZ, out Vector3 point))
        {
            Error = $"Invalid point: {input}. Use a distance, x,y, x,y,z, @dx,dy,dz, "
                  + "@distance<angle, or pick in the viewport.";
            return false;
        }

        Value = point;
        Status = PromptStatus.OK;
        return true;
    }

    /// <summary>Parses "x,y" or "x,y,z"; also used where a command reads a triple of its own.</summary>
    public static bool TryParsePoint(string input, out Vector3 point)
        => TryParsePoint(input, null, 0d, out point);

    /// <summary>
    /// Parses absolute Cartesian coordinates, AutoCAD-style relative coordinates prefixed
    /// with <c>@</c>, or a relative polar value in the form <c>@distance&lt;angle</c>.
    /// Polar angles are measured counter-clockwise in the active XY plane.
    /// </summary>
    public static bool TryParsePoint(string input, Vector3? basePoint, double planeZ, out Vector3 point)
    {
        point = default;
        string text = input.Trim();
        bool relative = text.StartsWith('@');
        if (relative)
        {
            if (basePoint == null)
                return false;
            text = text[1..].Trim();
        }

        int polarSeparator = text.IndexOf('<');
        if (polarSeparator >= 0)
        {
            if (!relative || polarSeparator == 0 || polarSeparator == text.Length - 1
                || !double.TryParse(text[..polarSeparator], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double distance)
                || !double.TryParse(text[(polarSeparator + 1)..], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double angleDegrees)
                || !double.IsFinite(distance) || !double.IsFinite(angleDegrees))
                return false;

            double angle = angleDegrees * Math.PI / 180d;
            Vector3 origin = basePoint!.Value;
            point = origin + new Vector3(
                (float)(distance * Math.Cos(angle)),
                (float)(distance * Math.Sin(angle)),
                0f);
            return true;
        }

        string[] parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is not (2 or 3))
            return false;

        var values = new float[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }

        if (relative)
        {
            Vector3 origin = basePoint!.Value;
            point = origin + new Vector3(values[0], values[1], parts.Length == 3 ? values[2] : 0f);
        }
        else
        {
            point = new Vector3(values[0], values[1], parts.Length == 3 ? values[2] : (float)planeZ);
        }
        return true;
    }
}

/// <summary>A prompt for a viewport pixel position, typed as "x,y" or clicked.</summary>
public sealed class PromptScreenPointStep(string message) : PromptStep(message, allowArbitraryInput: true)
{
    public int X { get; internal set; }
    public int Y { get; internal set; }

    internal override bool Accept(string input)
    {
        string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            Error = $"Invalid point: {input}. Use x,y in pixels, or click in the viewport.";
            return false;
        }

        X = x;
        Y = y;
        Status = PromptStatus.OK;
        return true;
    }
}

/// <summary>
/// A prompt for a file or directory path. The shell may answer it with a native file dialog;
/// typing the path does the same thing, which is what keeps OPEN scriptable.
/// </summary>
public sealed class PromptFileStep(string message, PromptFileMode mode) : PromptStep(message, allowArbitraryInput: true)
{
    public string Value { get; private set; } = "";
    public PromptFileMode Mode { get; } = mode;
    public bool MustExist { get; private set; }

    /// <summary>File extension filter the shell's dialog should apply, e.g. "las".</summary>
    public string Filter { get; private set; } = "";

    public PromptFileStep Requiring()
    {
        MustExist = true;
        return this;
    }

    public PromptFileStep Filtering(string filter)
    {
        Filter = filter;
        return this;
    }

    internal override bool Accept(string input)
    {
        string path = CommandText.Unquote(input);
        if (path.Length == 0)
        {
            Error = "A path is required.";
            return false;
        }

        if (MustExist && Mode == PromptFileMode.OpenFile && !File.Exists(path))
        {
            Error = $"File not found: {path}";
            return false;
        }

        if (MustExist && Mode == PromptFileMode.Directory && !Directory.Exists(path))
        {
            Error = $"Directory not found: {path}";
            return false;
        }

        Value = path;
        Status = PromptStatus.OK;
        return true;
    }
}

public enum PromptFileMode
{
    OpenFile,
    SaveFile,
    Directory
}
