namespace CloudScope.Commands;

/// <summary>
/// Formats the registered command table for command-line output and documentation tools.
/// It deliberately accepts descriptors rather than a hand-written list: aliases, scope and
/// syntax are therefore reported from the same registration that executes commands.
/// </summary>
public static class CommandInventory
{
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
