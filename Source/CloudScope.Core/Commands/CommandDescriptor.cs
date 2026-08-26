namespace CloudScope.Commands;

/// <summary>
/// A registered command as the rest of the program sees it. This is the single source of
/// truth behind <c>HELP</c>, autocomplete, the menu's tooltips and the coverage report —
/// each of which used to keep its own copy of what a command is called and does.
/// </summary>
public sealed record CommandDescriptor(
    string GlobalName,
    IReadOnlyList<string> Aliases,
    CommandFlags Flags,
    CommandGroup Group,
    CommandScope Scope,
    string Summary,
    string Syntax)
{
    /// <summary>
    /// The method that implements the command. The table was built by reflection, and the
    /// coverage report reads the method's IL to see which viewer API a command actually
    /// reaches — which is what keeps the report honest rather than hand-maintained.
    /// </summary>
    public System.Reflection.MethodInfo? Implementation { get; init; }

    /// <summary>Usage line, falling back to the bare command name when none was declared.</summary>
    public string UsageLine => Syntax.Length > 0 ? Syntax : GlobalName;

    public string AliasText => Aliases.Count == 0 ? "" : string.Join(", ", Aliases);

    /// <summary>Single-line form used by command lists: "ZOOM (Z) — Zooms the view."</summary>
    public string ListLine =>
        Aliases.Count == 0 ? $"{GlobalName} — {Summary}" : $"{GlobalName} ({AliasText}) — {Summary}";
}
