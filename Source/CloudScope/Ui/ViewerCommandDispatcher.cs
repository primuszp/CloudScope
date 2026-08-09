using CloudScope.Ui.Commands;
using CloudScope.Commands;

namespace CloudScope.Ui;

public sealed class ViewerCommandDispatcher : ICommandExecutor
{
    private readonly CommandRuntime _runtime;
    public CommandLineSession Session { get; }

    public ViewerCommandDispatcher(ViewerController viewer)
    {
        var commands = new ViewerCommands();
        _runtime = new CommandRuntime(viewer, commands);
        commands.Executor = _runtime;
        Session = new CommandLineSession(this);
    }

    public event EventHandler<CommandEventArgs>
        CommandStarted { add => _runtime.CommandStarted += value; remove => _runtime.CommandStarted -= value; }
    public event EventHandler<CommandEventArgs>
        CommandEnded { add => _runtime.CommandEnded += value; remove => _runtime.CommandEnded -= value; }
    public event EventHandler<CommandEventArgs>
        CommandCancelled { add => _runtime.CommandCancelled += value; remove => _runtime.CommandCancelled -= value; }
    public event EventHandler<CommandEventArgs>
        CommandFailed { add => _runtime.CommandFailed += value; remove => _runtime.CommandFailed -= value; }

    public string CurrentPrompt => _runtime.CurrentPrompt;
    public bool IsKnownCommand(string name) => _runtime.IsKnownCommand(name);
    public string ResolveGlobalName(string name) => _runtime.ResolveGlobalName(name);
    public IReadOnlyCollection<string> KnownCommandNames => _runtime.KnownCommandNames;
    public bool HasActiveCommand => _runtime.HasActiveCommand;
    public PromptOptions? ActiveOptions => _runtime.ActiveOptions;
    public bool IsTransparentCommand(string name) => _runtime.IsTransparentCommand(name);
    public CommandResult Execute(string input) => _runtime.Execute(input);
    public CommandResult CancelActive() => _runtime.CancelActive();

    public bool TryExecuteShortcut(ViewerKey key, bool ctrl)
    {
        if (!ctrl && key is ViewerKey.Enter or ViewerKey.Space)
        {
            Execute("");
            return true;
        }

        return false;
    }
}
