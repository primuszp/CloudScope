namespace CloudScope.Commands;

/// <summary>
/// Formats the registered command table for command-line output and documentation tools.
/// It deliberately accepts descriptors rather than a hand-written list: aliases, scope and
/// syntax are therefore reported from the same registration that executes commands.
/// </summary>
public static class CommandInventory
{
    /// <summary>Documentation generated from the same descriptors the runtime executes.</summary>
    public static string Markdown(IReadOnlyCollection<CommandDescriptor> commands)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("# CloudScope parancs-nyilvántartás");
        text.AppendLine();
        text.AppendLine("Ez a fájl a futásidejű parancstáblából készül. Újragenerálás a repó gyökeréből:");
        text.AppendLine();
        text.AppendLine("```bash");
        text.AppendLine("dotnet run --project Source/CloudScope.CommandChecks -- --write-inventory Docs/CommandInventory.md");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine($"Összesen **{commands.Count} parancs**, aliasokkal együtt **{commands.Sum(c => 1 + c.Aliases.Count)} név**.");
        text.AppendLine("A `COMMANDS` és a `HELP` ugyanebből a regisztrációból dolgozik. A `COVERAGE` a viewer API statikus elérhetőségét méri; a működés ellenőrzését a CommandChecks végzi.");
        text.AppendLine();
        text.AppendLine("Az argumentumokat szóköz választja el. A szóközt tartalmazó útvonalat vagy egy argumentumként kezelt nevet idézőjelbe kell tenni, például `OPENXYZ \"scan one.xyz\" 5000` vagy `LABEL \"Tree 1\" 7`. Külön útvonalbekéréskor a teljes begépelt válasz az útvonal.");
        text.AppendLine();
        text.AppendLine("A parancsok és a szkriptek közös futtatót használnak. A szkript üres sora az aktív bekérés alapértékét fogadja el; nyugalmi állapotban nem ismétel parancsot. A beágyazott szkript a hívó folytatása előtt fut le. Ismeretlen parancs vagy érvénytelen numerikus válasz megszakítja a szkriptet. Egy futtatás legfeljebb 100 000 sort dolgoz fel.");
        text.AppendLine();
        foreach (var group in commands.GroupBy(c => c.Group).OrderBy(g => g.Key))
        {
            text.AppendLine($"## {group.Key} ({group.Count()})");
            text.AppendLine();
            text.AppendLine("| Parancs | Alias | Hatókör | Szintaxis | Művelet |");
            text.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var command in group.OrderBy(c => c.GlobalName, StringComparer.OrdinalIgnoreCase))
                text.AppendLine($"| `{command.GlobalName}` | {(command.Aliases.Count == 0 ? "–" : string.Join(", ", command.Aliases))} | {command.Scope} | `{Escape(command.Syntax)}` | {Escape(command.Summary)} |");
            text.AppendLine();
        }
        text.AppendLine("A folyamatos egér- és billentyűgesztusok bemeneti események; a megfelelő műveletek parancsból is elérhetők. A rendszer részletei: [CommandSystem.md](CommandSystem.md).");
        return text.ToString().ReplaceLineEndings("\n");

        static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
    }

    public static string Describe(IEnumerable<CommandDescriptor> commands)
    {
        var lines = new List<string>
        {
            "COMMAND  ALIASES  SCOPE        USAGE",
            "-------  -------  -----------  -----"
        };

        foreach (CommandDescriptor command in commands
            .OrderBy(command => command.Group)
            .ThenBy(command => command.GlobalName, StringComparer.OrdinalIgnoreCase))
        {
            string aliases = command.Aliases.Count == 0 ? "-" : string.Join(",", command.Aliases);
            lines.Add($"{command.GlobalName,-7}  {aliases,-7}  {command.Scope,-11}  {command.UsageLine}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
