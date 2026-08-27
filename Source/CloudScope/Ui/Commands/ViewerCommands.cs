using CloudScope.Commands;
using CloudScope.Loading;
using System.Globalization;

namespace CloudScope.Ui.Commands;

/// <summary>
/// Every command the viewer registers. The class is split by command group across
/// <c>ViewerCommands.*.cs</c>; this file holds the keyword sets and parsers they share.
/// </summary>
/// <remarks>
/// A command body is a coroutine: it yields the question it wants answered and reads the
/// answer on the next line. Nothing about a half-finished command lives in a field here, so
/// cancelling one is the runtime disposing its iterator — there is no per-command reset.
/// </remarks>
public sealed partial class ViewerCommands
{
    /// <summary>
    /// Set by the dispatcher after registration so HELP and SCRIPT can work against what is
    /// actually registered. A hand-written list is a second source of truth that silently rots.
    /// </summary>
    internal ICommandExecutor? Executor { get; set; }

    // ----- Keyword sets (AutoCAD-style: capitalised letters define the abbreviation) -----

    private static readonly Keyword[] SelectKeywords =
    [
        new("BOX", "Box"),
        new("SPHERE", "Sphere", "SPH"),
        new("CYLINDER", "Cylinder", "CYL"),
        new("UNDO", "Undo"),
        new("CONFIRM", "CONFirm"),
        new("CANCEL", "CANcel"),
        new("FIT", "Fit"),
        new("FITGROUND", "GroundFit", "GF", "FITG")
    ];

    private static readonly Keyword[] ViewKeywords =
    [
        new("FRONT", "Front"),
        new("BACK", "BAck"),
        new("LEFT", "Left"),
        new("RIGHT", "Right"),
        new("TOP", "Top"),
        new("BOTTOM", "Bottom", "BO", "BOT"),
        new("ISOMETRIC", "Isometric", "ISO"),
        new("SAVE", "Save"),
        new("RESTORE", "Restore"),
        new("LIST", "LIst"),
        new("DELETE", "DElete")
    ];

    private static readonly Keyword[] ProjectionKeywords =
    [
        new("PERSPECTIVE", "Perspective"),
        new("PARALLEL", "PArallel", "ORTHO", "ORTHOGRAPHIC")
    ];

    private static readonly Keyword SourceKeyword = new("SOURCE", "Source");

    private static readonly Keyword[] LayerKeywords =
    [
        new("LIST", "List"),
        new("ON", "ON"),
        new("OFF", "OFf"),
        new("CLOSE", "Close")
    ];

    private static readonly Keyword[] ColorKeywords =
    [
        new("RGB", "Rgb"),
        new("HEIGHT", "Height", "Z"),
        new("CLASS", "Class"),
        new("INTENSITY", "Intensity"),
        new("RETURN", "ReTurn"),
        new("CLEAR", "CLear")
    ];

    private static readonly Keyword[] ZoomKeywords =
    [
        new("ALL", "All"),
        new("CENTER", "Center"),
        new("EXTENTS", "Extents"),
        new("OBJECT", "Object"),
        new("WINDOW", "Window")
    ];

    private static readonly Keyword[] FilterKeywords =
    [
        new("CLASS", "Class"),
        new("INTENSITY", "Intensity"),
        new("RETURN", "Return"),
        new("Z", "Z", "HEIGHT"),
        new("CLEAR", "CLear")
    ];

    private static readonly Keyword[] FilterValueKeywords =
    [
        new("LIST", "List"),
        new("BACK", "Back")
    ];

    private static readonly Keyword[] ViewportLayoutKeywords =
    [
        new("SINGLE", "Single"),
        new("TWO", "Two", "2"),
        new("FOUR", "Four", "4"),
        new("PLAN", "Plan", "TOP"),
        new("PREVIOUS", "PRevious")
    ];

    private static readonly Keyword[] ViewportArrangementKeywords =
    [
        new("VERTICAL", "Vertical"),
        new("HORIZONTAL", "Horizontal")
    ];

    private static readonly Keyword[] ViewportViewKeywords =
    [
        new("TOP", "Top"),
        new("BOTTOM", "Bottom", "BO", "BOT"),
        new("LEFT", "Left"),
        new("RIGHT", "Right"),
        new("FRONT", "Front"),
        new("BACK", "BAck"),
        new("ISOMETRIC", "Isometric", "ISO")
    ];

    private static readonly Keyword ClearKeyword = new("CLEAR", "CLear", "NONE");

    // Remembered between invocations on purpose: these are the "<default>" shown in the
    // prompt, exactly like AutoCAD's sticky last-used option. They are not command phase.
    private ViewportLayoutKind _lastViewportLayout = ViewportLayoutKind.SinglePerspective;
    private ViewportLayoutKind _lastViewportArrangement = ViewportLayoutKind.TwoVertical;
    private ViewportViewKind _lastViewportView = ViewportViewKind.Top;

    // ----- Shared parsers -----

    private static ColorSource MapColorSource(string globalKeyword) => globalKeyword switch
    {
        "HEIGHT" => ColorSource.Height,
        "CLASS" => ColorSource.Class,
        "INTENSITY" => ColorSource.Intensity,
        "RETURN" => ColorSource.Return,
        _ => ColorSource.Rgb
    };

    private static bool TryParseZoomFactor(string input, out float factor)
    {
        string value = input.Trim();
        if (value.EndsWith("XP", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        else if (value.EndsWith('X') || value.EndsWith('x'))
            value = value[..^1];

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out factor) && factor > 0f;
    }

    private static bool TryParseScreenPoint(string input, out int x, out int y)
    {
        x = 0;
        y = 0;
        string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float fx) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fy))
            return false;

        x = (int)MathF.Round(fx);
        y = (int)MathF.Round(fy);
        return true;
    }

    private static bool TryParseByteList(string text, out byte[] values)
    {
        values = [];
        string[] tokens = text
            .Replace(';', ',')
            .Replace(' ', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parsed = new List<byte>();
        foreach (string token in tokens)
        {
            if (!byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value))
                return false;
            parsed.Add(value);
        }

        values = parsed.Distinct().ToArray();
        return values.Length > 0;
    }

    /// <summary>Parses "a,b" or "a b" keeping the order typed.</summary>
    private static bool TryParsePair(string text, out double first, out double second)
    {
        first = 0;
        second = 0;
        string[] parts = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out first) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out second);
    }

    /// <summary>Parses a pair as a range, ordering it low to high.</summary>
    private static bool TryParseRange(string text, out double min, out double max)
    {
        if (!TryParsePair(text, out min, out max))
            return false;

        if (min > max) (min, max) = (max, min);
        return true;
    }

    /// <summary>Splits "&lt;name&gt; [instance id]", where the name may be quoted.</summary>
    private static bool TryParseLabelAnnotation(
        string input, out string name, out int? instanceId, out bool instanceSpecified, out string error)
    {
        name = "";
        instanceId = null;
        instanceSpecified = false;
        error = "";

        string trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            error = "Label name must not be empty.";
            return false;
        }

        if (trimmed[0] == '"')
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote < 0)
            {
                error = "Missing closing quote in label name.";
                return false;
            }

            name = trimmed[1..endQuote].Trim();
            string rest = trimmed[(endQuote + 1)..].Trim();
            if (rest.Length > 0)
            {
                instanceSpecified = true;
                if (!TryParseInstanceToken(rest, out instanceId))
                {
                    error = $"Invalid instance id: {rest}";
                    return false;
                }
            }
        }
        else
        {
            int split = trimmed.LastIndexOf(' ');
            if (split > 0 && TryParseInstanceToken(trimmed[(split + 1)..].Trim(), out instanceId))
            {
                instanceSpecified = true;
                name = trimmed[..split].Trim();
            }
            else
            {
                name = trimmed;
            }
        }

        name = CommandText.Unquote(name);
        if (name.Length > 0)
            return true;

        error = "Label name must not be empty.";
        return false;
    }

    private static bool TryParseInstanceToken(string token, out int? instanceId)
    {
        instanceId = null;
        if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
            return false;

        instanceId = parsed;
        return true;
    }
}
