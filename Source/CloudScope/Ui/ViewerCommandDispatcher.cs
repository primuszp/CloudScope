using CloudScope.Ui.Commands;
using CloudScope.Commands;

namespace CloudScope.Ui;

public sealed class ViewerCommandDispatcher : ICommandExecutor
{
    private readonly CommandRuntime _runtime;
    public CommandLineSession Session { get; }

    public ViewerCommandDispatcher(ViewerController viewer)
    {
        _runtime = new CommandRuntime(viewer, new ViewerCommands());
        Session = new CommandLineSession(ExecuteResult, () => CurrentPrompt);
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
    public IReadOnlyCollection<string> KnownCommandNames => _runtime.KnownCommandNames;
    public bool HasActiveCommand => _runtime.HasActiveCommand;
    public bool IsTransparentCommand(string name) => _runtime.IsTransparentCommand(name);
    public CommandResult ExecuteResult(string commandText) => _runtime.Execute(commandText);
    public CommandResult Execute(string input) => ExecuteResult(input);
    public CommandResult CancelActive() => _runtime.CancelActive();
    public string ExecuteCommand(string commandText) => ExecuteResult(commandText).Message;

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
