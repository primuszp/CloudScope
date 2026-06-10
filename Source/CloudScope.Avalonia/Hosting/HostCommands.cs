using CloudScope.Commands;

namespace CloudScope.Avalonia.Hosting;

internal sealed class HostCommands
{
    private readonly HostController _host;

    public HostCommands(HostController host) => _host = host;

    [CommandMethod("STATUS", Flags = CommandFlags.NoHistory | CommandFlags.NoUndoMarker | CommandFlags.Transparent)]
    public CommandResult Status(CommandContext context) => CommandResult.End(_host.Status);

    [CommandMethod("RESET", Flags = CommandFlags.NoUndoMarker)]
    public CommandResult Reset(CommandContext context)
    {
        _host.PerformReset();
        return CommandResult.End("Host controller reset.");
    }

    [CommandMethod("HOSTHELP", Flags = CommandFlags.NoHistory | CommandFlags.NoUndoMarker | CommandFlags.Transparent)]
    public CommandResult HostHelp(CommandContext context) =>
        CommandResult.End("Host commands: STATUS, RESET, HOSTHELP. Type HELP for viewer commands.");
}
