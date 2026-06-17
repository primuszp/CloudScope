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

    public void Register(ICommandExecutor executor) => _executors.Add(executor);

    public bool IsKnownCommand(string name) => _executors.Any(executor => executor.IsKnownCommand(name));

    public CommandResult Execute(string input)
    {
        string firstWord = CommandText.FirstWord(input);
        if (firstWord.Length == 0)
        {
            ICommandExecutor? executor = _activeExecutor ?? _repeatExecutor ?? _executors.LastOrDefault();
            if (executor == null)
                return _fallback?.Invoke(input) ?? CommandResult.End();

            CommandResult result = executor.Execute(input);
            TrackExecutorState(executor, result);
            return result;
        }

        foreach (ICommandExecutor executor in _executors)
        {
            if (!executor.IsKnownCommand(firstWord))
                continue;

            CommandResult result = executor.Execute(input);
            TrackExecutorState(executor, result);
            return result;
        }

        return _fallback?.Invoke(input)
            ?? CommandResult.End($"Unknown command \"{firstWord}\". Press F1 or type HELP.");
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
