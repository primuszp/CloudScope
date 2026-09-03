namespace CloudScope.Commands;

/// <summary>
/// Applies the availability meaning of <see cref="CommandScope"/> consistently at the
/// command boundary. Hosts supply only the facts they own; wording and scope semantics do
/// not get reimplemented in each UI shell.
/// </summary>
public static class CommandScopePolicy
{
    public static string? ExplainUnavailable(
        CommandDescriptor command,
        bool viewerAvailable,
        bool documentAvailable) => command.Scope switch
        {
            CommandScope.Viewer when !viewerAvailable =>
                $"{command.GlobalName} requires an available viewer.",
            CommandScope.Document when !viewerAvailable =>
                $"{command.GlobalName} requires an available viewer.",
            CommandScope.Document when !documentAvailable =>
                $"{command.GlobalName} requires an open point cloud. Use OPEN or OPENSTORE first.",
            _ => null
        };
}
