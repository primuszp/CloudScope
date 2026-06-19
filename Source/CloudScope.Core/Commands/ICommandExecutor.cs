namespace CloudScope.Commands;

public interface ICommandExecutor
{
    string CurrentPrompt { get; }
    bool IsKnownCommand(string name);

    /// <summary>Names (and aliases) this executor can run; used to detect cross-executor collisions.</summary>
    IReadOnlyCollection<string> KnownCommandNames { get; }

    /// <summary>True while a modal command on this executor is mid-prompt.</summary>
    bool HasActiveCommand { get; }

    bool IsTransparentCommand(string name);

    /// <summary>Cancels the executor's active command, if any.</summary>
    CommandResult CancelActive();

    CommandResult Execute(string input);
}
