using CloudScope.Ui.Commands;
using CloudScope.Commands;

namespace CloudScope.Ui;

public sealed class ViewerCommandDispatcher : ICommandExecutor, ICommandOutputSource
{
    private readonly CommandRuntime _runtime;
    public CommandLineSession Session { get; }

    public ViewerCommandDispatcher(ViewerController viewer)
    {
        var commands = new ViewerCommands();
        _runtime = new CommandRuntime(viewer, commands);
        commands.Executor = _runtime;
        Session = new CommandLineSession(this);
        BindUndoMarks(viewer);
        viewer.CommandPrompts = this;

        // A load finishing reports itself on the command line through the same channel a
        // viewport pick uses, so a shell needs one way in for output it did not ask for.
        viewer.BackgroundMessage += message => Publish(CommandResult.End(message));
    }

    /// <summary>
    /// Turns the command lifecycle into undo steps: a command that changes something opens a
    /// mark when it starts, commits it when it ends, and rolls it back when it is cancelled or
    /// fails. Commands flagged <see cref="CommandFlags.NoUndoMarker"/> — the inquiry and view
    /// commands — open none, so stepping back never lands on a ZOOM.
    /// </summary>
    private void BindUndoMarks(ViewerController viewer)
    {
        UndoManager history = viewer.UndoHistory;

        _runtime.CommandStarted += (_, e) =>
        {
            if (!MarksUndo(e.GlobalCommandName)) return;
            history.BeginMark(e.GlobalCommandName);
        };

        _runtime.CommandEnded += (_, e) =>
        {
            if (!MarksUndo(e.GlobalCommandName)) return;
            history.Commit();
        };

        _runtime.CommandCancelled += (_, e) =>
        {
            if (!MarksUndo(e.GlobalCommandName)) return;
            history.Rollback();
        };

        _runtime.CommandFailed += (_, e) =>
        {
            if (!MarksUndo(e.GlobalCommandName)) return;
            history.Rollback();
        };
    }

    private bool MarksUndo(string commandName) =>
        _runtime.TryGetCommand(commandName, out CommandDescriptor command) &&
        !command.Flags.HasFlag(CommandFlags.NoUndoMarker);

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
    public IReadOnlyCollection<CommandDescriptor> Commands => _runtime.Commands;
    public bool TryGetCommand(string name, out CommandDescriptor command) => _runtime.TryGetCommand(name, out command);
    public bool HasActiveCommand => _runtime.HasActiveCommand;
    public PromptOptions? ActiveOptions => _runtime.ActiveOptions;
    public bool IsTransparentCommand(string name) => _runtime.IsTransparentCommand(name);
    public CommandResult Execute(string input) => _runtime.Execute(input);
    public CommandResult CancelActive() => _runtime.CancelActive();
    public PromptStep? ActiveStep => _runtime.ActiveStep;
    public bool AwaitsPoint => _runtime.AwaitsPoint;
    public PromptStep? PointPrompt => _runtime.PointPrompt;

    /// <summary>Raised when a viewport pick answers a prompt, so the command line can echo it.</summary>
    public event Action<CommandResult>? OutputProduced;

    public CommandResult SupplyPoint(OpenTK.Mathematics.Vector3 world, int screenX, int screenY)
    {
        PromptStep? answered = _runtime.PointPrompt;
        CommandResult result = _runtime.SupplyPoint(world, screenX, screenY);

        if (answered != null)
            Session.AddHistory($"Command: {answered.Text}", CommandEntryKind.Echo);

        return Publish(result);
    }

    /// <summary>Echoes output nobody submitted onto this session, and offers it to the shell.</summary>
    private CommandResult Publish(CommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
            Session.AddHistory(result.Message);
        if (result.Status == CommandStatus.Prompting && !string.IsNullOrWhiteSpace(result.Prompt))
            Session.AddHistory(result.Prompt, CommandEntryKind.Prompt);

        OutputProduced?.Invoke(result);
        return result;
    }

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
