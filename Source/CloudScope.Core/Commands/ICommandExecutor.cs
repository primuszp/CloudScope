namespace CloudScope.Commands;

public interface ICommandExecutor
{
    string CurrentPrompt { get; }
    bool IsKnownCommand(string name);

    /// <summary>Names (and aliases) this executor can run; used to detect cross-executor collisions.</summary>
    IReadOnlyCollection<string> KnownCommandNames { get; }

    /// <summary>True while a modal command on this executor is mid-prompt.</summary>
    bool HasActiveCommand { get; }

    /// <summary>
    /// Keyword/input policy of the prompt currently awaiting a response, or null when no
    /// command is prompting. UIs use it to render clickable keywords and to decide whether
    /// Space submits.
    /// </summary>
    PromptOptions? ActiveOptions => null;

    bool IsTransparentCommand(string name);

    /// <summary>
    /// The global command name a registered name belongs to, or "" when the name is unknown.
    /// A name that differs from its global name is an alias, which the command line shows as
    /// "alias of ZOOM" and ranks below full command names.
    /// </summary>
    string ResolveGlobalName(string name) => "";

    /// <summary>Cancels the executor's active command, if any.</summary>
    CommandResult CancelActive();

    CommandResult Execute(string input);
}
