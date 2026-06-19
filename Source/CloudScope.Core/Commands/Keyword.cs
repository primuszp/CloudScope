namespace CloudScope.Commands;

/// <summary>
/// An AutoCAD-style prompt keyword. A keyword has a global (culture-independent)
/// name used by command logic, a display string whose capitalised letters define
/// the accepted abbreviation (e.g. "CONFirm" accepts "CONF"), and optional extra
/// aliases. Matching is case-insensitive against the global name, the full display
/// word, the derived abbreviation, and any explicit alias.
/// </summary>
public sealed class Keyword
{
    public Keyword(string globalName, string? display = null, params string[] aliases)
    {
        GlobalName = globalName.ToUpperInvariant();
        Display = string.IsNullOrEmpty(display) ? Capitalize(GlobalName) : display;
        Abbreviation = ExtractAbbreviation(Display);
        Aliases = aliases.Select(a => a.ToUpperInvariant()).ToArray();
    }

    /// <summary>Culture-independent name command logic switches on.</summary>
    public string GlobalName { get; }

    /// <summary>Display word; capitalised letters define the abbreviation.</summary>
    public string Display { get; }

    /// <summary>Abbreviation derived from the capitalised letters of <see cref="Display"/>.</summary>
    public string Abbreviation { get; }

    public IReadOnlyList<string> Aliases { get; }

    public bool Matches(string input)
    {
        string s = input.Trim();
        if (s.Length == 0)
            return false;

        if (s.Equals(GlobalName, StringComparison.OrdinalIgnoreCase) ||
            s.Equals(Display, StringComparison.OrdinalIgnoreCase) ||
            (Abbreviation.Length > 0 && s.Equals(Abbreviation, StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (string alias in Aliases)
            if (s.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static string ExtractAbbreviation(string display)
    {
        Span<char> buffer = stackalloc char[display.Length];
        int length = 0;
        foreach (char c in display)
            if (char.IsUpper(c))
                buffer[length++] = c;
        return length == 0 ? "" : new string(buffer[..length]);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
