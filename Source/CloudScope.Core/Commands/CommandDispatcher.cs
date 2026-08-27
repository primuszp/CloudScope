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

    public PromptOptions? ActiveOptions => _activeExecutor?.ActiveOptions;

    public IReadOnlyCollection<string> KnownCommandNames =>
        _executors.SelectMany(executor => executor.KnownCommandNames).ToArray();

    public IReadOnlyCollection<CommandDescriptor> Commands =>
        _executors.SelectMany(executor => executor.Commands).ToArray();

    public IReadOnlyCollection<SystemVariable> Variables =>
        _executors.SelectMany(executor => executor.Variables).ToArray();

    public bool TryGetCommand(string name, out CommandDescriptor command)
    {
        foreach (ICommandExecutor executor in _executors)
            if (executor.TryGetCommand(name, out command))
                return true;

        command = null!;
        return false;
    }

    public void Register(ICommandExecutor executor)
    {
        string? conflict = FindShadowedName(executor);
        if (conflict != null)
            throw new InvalidOperationException(
                $"Command name or alias '{conflict}' is already registered by another executor and would be shadowed.");

        _executors.Add(executor);
        _shadowingChecked = _executors.All(e => e.KnownCommandNames.Count > 0);
    }

    private string? FindShadowedName(ICommandExecutor executor)
    {
        foreach (string name in executor.KnownCommandNames)
        {
            if (_executors.Any(e => !ReferenceEquals(e, executor) && e.IsKnownCommand(name)))
                return name;
        }

        return null;
    }

    // An executor whose command surface is created later (an embedded viewer host) reports no
    // names at registration, so the shadowing check has nothing to compare and must run again
    // once every executor is populated. It is reported rather than thrown, because by then it
    // would surface in the middle of a session.
    private bool _shadowingChecked;

    private string? CheckShadowingOnce()
    {
        if (_shadowingChecked || _executors.Any(e => e.KnownCommandNames.Count == 0))
            return null;

        _shadowingChecked = true;
        foreach (ICommandExecutor executor in _executors)
        {
            string? conflict = FindShadowedName(executor);
            if (conflict != null)
                return $"Command '{conflict}' is registered by more than one executor and is shadowed. " +
                       "Rename one of them; only the first registration will run.";
        }

        return null;
    }

    public bool IsKnownCommand(string name) => _executors.Any(executor => executor.IsKnownCommand(name));

    public string ResolveGlobalName(string name) =>
        _executors.FirstOrDefault(executor => executor.IsKnownCommand(name))?.ResolveGlobalName(name) ?? "";

    public bool IsTransparentCommand(string name) =>
        _executors.Any(executor => executor.IsKnownCommand(name) && executor.IsTransparentCommand(name));

    public PromptStep? ActiveStep => _activeExecutor?.ActiveStep;

    public bool AwaitsPoint => _activeExecutor?.AwaitsPoint == true;

    public PromptStep? PointPrompt => _activeExecutor?.PointPrompt;

    public CommandResult SupplyPoint(OpenTK.Mathematics.Vector3 world, int screenX, int screenY)
    {
        if (_activeExecutor == null) return CommandResult.End();
        CommandResult result = _activeExecutor.SupplyPoint(world, screenX, screenY);
        TrackExecutorState(_activeExecutor, result);
        return result;
    }

    public CommandResult CancelActive()
    {
        CommandResult result = _activeExecutor?.CancelActive() ?? CommandResult.Cancel();
        _activeExecutor = null;
        return result;
    }

    public CommandResult Execute(string input)
    {
        if (CheckShadowingOnce() is { } shadowing)
            return new CommandResult(CommandStatus.Failed, shadowing);

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
