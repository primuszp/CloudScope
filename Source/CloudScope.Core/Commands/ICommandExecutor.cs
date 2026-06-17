namespace CloudScope.Commands;

public interface ICommandExecutor
{
    string CurrentPrompt { get; }
    bool IsKnownCommand(string name);
    CommandResult Execute(string input);
}
