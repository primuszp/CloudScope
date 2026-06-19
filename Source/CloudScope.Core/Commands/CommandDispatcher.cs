namespace CloudScope.Commands;

public sealed class CommandDispatcher : ICommandExecutor
{
    private readonly List<ICommandExecutor> _executors = [];
    private readonly Func<string, CommandResult>? _fallback;
    private ICommandExecutor? _activeExecutor;
    private ICommandExecutor? _repeatExecutor;

    public CommandDispatcher(Func<string, CommandResult>? fallback = null)
    {
        _fallback = fallback;
    }

    public string CurrentPrompt => _activeExecutor?.CurrentPrompt ?? "Command:";

    public bool HasActiveCommand => _activeExecutor?.HasActiveCommand == true;

    public IReadOnlyCollection<string> KnownCommandNames =>
        _executors.SelectMany(executor => executor.KnownCommandNames).ToArray();

    public void Register(ICommandExecutor executor)
    {
        foreach (string name in executor.KnownCommandNames)
        {
            ICommandExecutor? owner = _executors.FirstOrDefault(e => e.IsKnownCommand(name));
            if (owner != null)
                throw new InvalidOperationException(
                    $"Command name or alias '{name}' is already registered by another executor and would be shadowed.");
        }

        _executors.Add(executor);
    }

    public bool IsKnownCommand(string name) => _executors.Any(executor => executor.IsKnownCommand(name));

    public bool IsTransparentCommand(string name) =>
        _executors.Any(executor => executor.IsKnownCommand(name) && executor.IsTransparentCommand(name));

    public CommandResult CancelActive()
    {
        CommandResult result = _activeExecutor?.CancelActive() ?? CommandResult.Cancel();
        _activeExecutor = null;
        return result;
    }

    public CommandResult Execute(string input)
    {
        string firstWord = CommandText.FirstWord(input);
        if (firstWord.Length == 0)
        {
            // Empty input (repeat / accept) goes to whichever executor owns the conversation.
            ICommandExecutor? executor = _activeExecutor ?? _repeatExecutor;
            if (executor == null)
                return _fallback?.Invoke(input) ?? CommandResult.End();

            return Run(executor, input);
        }

        foreach (ICommandExecutor executor in _executors)
        {
            if (!executor.IsKnownCommand(firstWord))
                continue;

            // Starting a new (non-transparent) command on a different executor cancels the one that
            // currently owns the modal conversation, so only one command is ever active across layers.
            if (_activeExecutor != null && !ReferenceEquals(_activeExecutor, executor) &&
                !executor.IsTransparentCommand(firstWord))
                CancelActive();

            return Run(executor, input);
        }

        return _fallback?.Invoke(input)
            ?? CommandResult.End($"Unknown command \"{firstWord}\". Press F1 or type HELP.");
    }

    private CommandResult Run(ICommandExecutor executor, string input)
    {
        CommandResult result = executor.Execute(input);
        TrackExecutorState(executor, result);
        return result;
    }

    private void TrackExecutorState(ICommandExecutor executor, CommandResult result)
    {
        if (result.Status == CommandStatus.Prompting)
        {
            _activeExecutor = executor;
            return;
        }

        if (ReferenceEquals(_activeExecutor, executor))
            _activeExecutor = null;

        if (result.Status == CommandStatus.Ended)
            _repeatExecutor = executor;
    }
}
