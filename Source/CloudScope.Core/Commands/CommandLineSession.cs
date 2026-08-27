namespace CloudScope.Commands;

/// <summary>
/// The single implementation of command-line behaviour shared by every shell:
/// staged text, submission, input recall, typed output history, keyword/command
/// completion, and the AutoCAD space-vs-enter submission rule.
/// </summary>
public sealed class CommandLineSession
{
    private readonly Func<string, CommandResult> _execute;
    private readonly Func<string> _prompt;
    private readonly ICommandExecutor? _executor;
    private readonly List<CommandLineEntry> _history = [];
    private readonly List<string> _inputHistory = [];
    private int _historyCursor;

    public CommandLineSession(
        Func<string, CommandResult> execute,
        Func<string> prompt,
        ICommandExecutor? executor = null,
        int historyLimit = 200)
    {
        _execute = execute;
        _prompt = prompt;
        _executor = executor;
        HistoryLimit = historyLimit;
        AddHistory("CloudScope command line ready. Type HELP for commands.", CommandEntryKind.Banner);
    }

    /// <summary>Convenience overload for a shell that drives one executor directly.</summary>
    public CommandLineSession(ICommandExecutor executor, int historyLimit = 200)
        : this(executor.Execute, () => executor.CurrentPrompt, executor, historyLimit)
    {
    }

    /// <summary>
    /// Raised whenever a line is added or the transcript is cleared. Shells that mirror the
    /// history — the expanded history window — follow it instead of polling or offering a
    /// "refresh" button for something that should never be stale.
    /// </summary>
    public event Action? HistoryChanged;

    public int HistoryLimit { get; }
    public string StagedText { get; private set; } = "";
    public string Prompt => _prompt();
    public IReadOnlyList<CommandLineEntry> History => _history;

    /// <summary>
    /// Total number of lines ever added. UIs compare it with what they have already shown to
    /// append only the new lines, instead of rebuilding the whole transcript per command.
    /// </summary>
    public long TotalEntries { get; private set; }

    /// <summary>Keyword/input policy of the prompt currently awaiting a response, if any.</summary>
    public PromptOptions? ActiveOptions => _executor?.ActiveOptions;

    /// <summary>
    /// True when Space submits like Enter. AutoCAD suppresses this while a prompt accepts
    /// arbitrary text, so paths and multi-word values stay typeable.
    /// </summary>
    public bool SpaceSubmits => ActiveOptions?.AllowArbitraryInput != true;

    /// <summary>Most recent inputs, newest first — for the command line's right-click menu.</summary>
    public IEnumerable<string> RecentInput => Enumerable.Reverse(_inputHistory);

    /// <summary>
    /// Most recent distinct command names, newest first. This is AutoCAD's "Recent Commands"
    /// list: the first word of each input, without its arguments.
    /// </summary>
    public IEnumerable<string> RecentCommands => RecentCommandNames;

    // Autocomplete asks for this on every keystroke, so the list is built once per submission
    // rather than once per character typed.
    private string[] RecentCommandNames => _recentCommands ??=
        Enumerable.Reverse(_inputHistory)
            .Select(CommandText.FirstWord)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string[]? _recentCommands;

    /// <summary>Restores input history from a previous session.</summary>
    public void SeedInputHistory(IEnumerable<string> inputs)
    {
        foreach (string input in inputs)
        {
            string trimmed = input.Trim();
            if (trimmed.Length > 0 && (_inputHistory.Count == 0 || _inputHistory[^1] != trimmed))
                _inputHistory.Add(trimmed);
        }

        _historyCursor = _inputHistory.Count;
        _recentCommands = null;
    }

    /// <summary>Snapshot of the input history, oldest first, for persisting between sessions.</summary>
    public IReadOnlyList<string> InputHistory => _inputHistory;

    /// <summary>Clears the visible output without touching input history.</summary>
    public void ClearHistory()
    {
        _history.Clear();
        TotalEntries = 0;
        HistoryChanged?.Invoke();
    }

    public void Stage(string command) => StagedText = command;

    /// <summary>Ranked completions for the text typed so far. Empty when nothing is typed.</summary>
    public IReadOnlyList<CommandCompletion> Complete(string prefix) =>
        CommandCompletionSource.Complete(prefix, ActiveOptions, _executor?.KnownCommandNames,
            _executor != null ? _executor.ResolveGlobalName : null,
            variables: _executor?.Variables,
            recentCommands: RecentCommandNames);

    public CommandResult Submit(string? text = null) => Submit(text, _execute);

    public CommandResult Submit(string? text, Func<string, CommandResult> execute)
    {
        string command = text ?? StagedText;
        AddHistory(CommandLineEntry.EchoPrefix + (command.Length == 0 ? "<repeat>" : command.Trim()),
            CommandEntryKind.Echo);
        Remember(command);
        CommandResult result = execute(command);
        if (!string.IsNullOrWhiteSpace(result.Message))
            AddHistory(result.Message, KindOf(result.Status));
        if (result.Status == CommandStatus.Prompting && !string.IsNullOrWhiteSpace(result.Prompt))
            AddHistory(result.Prompt, CommandEntryKind.Prompt);
        StagedText = "";
        return result;
    }

    public string Recall(int direction)
    {
        if (_inputHistory.Count == 0) return StagedText;
        _historyCursor = Math.Clamp(_historyCursor + direction, 0, _inputHistory.Count);
        return StagedText = _historyCursor == _inputHistory.Count ? "" : _inputHistory[_historyCursor];
    }

    public void AddHistory(string message) => AddHistory(message, CommandEntryKind.Output);

    public void AddHistory(string message, CommandEntryKind kind)
    {
        _history.Add(new CommandLineEntry(kind, message));
        TotalEntries++;
        int excess = _history.Count - HistoryLimit;
        if (excess > 0) _history.RemoveRange(0, excess);
        HistoryChanged?.Invoke();
    }

    public string HistoryText => string.Join(Environment.NewLine, _history.Select(entry => entry.Text));

    private static CommandEntryKind KindOf(CommandStatus status) => status switch
    {
        CommandStatus.Failed => CommandEntryKind.Error,
        CommandStatus.Cancelled => CommandEntryKind.Error,
        _ => CommandEntryKind.Output
    };

    private void Remember(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.Length > 0 && (_inputHistory.Count == 0 || _inputHistory[^1] != trimmed))
            _inputHistory.Add(trimmed);
        int excess = _inputHistory.Count - 100;
        if (excess > 0) _inputHistory.RemoveRange(0, excess);
        _historyCursor = _inputHistory.Count;
        _recentCommands = null;
    }
}
