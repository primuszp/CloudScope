using CloudScope.Commands;

namespace CloudScope.Avalonia.Hosting;

/// <summary>
/// Commands that act on the desktop shell rather than the viewer inside it. They share the
/// viewer's command table through the dispatcher, so a name registered here cannot collide
/// with a viewer command.
/// </summary>
internal sealed class HostCommands
{
    private readonly HostController _host;

    public HostCommands(HostController host) => _host = host;

    [CommandMethod("HOSTSTATUS", Flags = CommandFlags.NoHistory | CommandFlags.NoUndoMarker | CommandFlags.Transparent,
        Group = CommandGroup.Inquiry, Scope = CommandScope.Application,
        Summary = "Reports the shell's own status line.",
        Syntax = "HOSTSTATUS")]
    public IEnumerable<PromptStep> Status(CommandContext context)
    {
        context.Editor.WriteMessage(_host.StatusText);
        yield break;
    }
}
