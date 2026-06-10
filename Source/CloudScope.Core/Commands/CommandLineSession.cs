namespace CloudScope.Commands;

public sealed class CommandLineSession
{
    private readonly Func<string, CommandResult> _execute;
    private readonly Func<string> _prompt;
    private readonly List<string> _history = [];
    private readonly List<string> _inputHistory = [];
    private int _historyCursor;

    public CommandLineSession(Func<string, CommandResult> execute, Func<string> prompt, int historyLimit = 200)
    {
        _execute = execute;
        _prompt = prompt;
        HistoryLimit = historyLimit;
        AddHistory("CloudScope command line ready. Type HELP for commands.");
    }

    public int HistoryLimit { get; }
    public string StagedText { get; private set; } = "";
    public string Prompt => _prompt();
    public IReadOnlyList<string> History => _history;

    public void Stage(string command) => StagedText = command;

    public CommandResult Submit(string? text = null)
    {
        string command = text ?? StagedText;
        AddHistory(command.Length == 0 ? "Command: <repeat>" : $"Command: {command.Trim()}");
        Remember(command);
        CommandResult result = _execute(command);
        if (!string.IsNullOrWhiteSpace(result.Message)) AddHistory(result.Message);
        StagedText = "";
        return result;
    }

    public string Recall(int direction)
    {
        if (_inputHistory.Count == 0) return StagedText;
        _historyCursor = Math.Clamp(_historyCursor + direction, 0, _inputHistory.Count);
        return StagedText = _historyCursor == _inputHistory.Count ? "" : _inputHistory[_historyCursor];
    }

    public void AddHistory(string message)
    {
        _history.Add(message);
        int excess = _history.Count - HistoryLimit;
        if (excess > 0) _history.RemoveRange(0, excess);
    }

    private void Remember(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.Length > 0 && (_inputHistory.Count == 0 || _inputHistory[^1] != trimmed))
            _inputHistory.Add(trimmed);
        int excess = _inputHistory.Count - 100;
        if (excess > 0) _inputHistory.RemoveRange(0, excess);
        _historyCursor = _inputHistory.Count;
    }
}
