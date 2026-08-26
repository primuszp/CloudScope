namespace CloudScope.Commands;

public interface ICommandExecutor
{
    string CurrentPrompt { get; }
    bool IsKnownCommand(string name);

    /// <summary>Names (and aliases) this executor can run; used to detect cross-executor collisions.</summary>
    IReadOnlyCollection<string> KnownCommandNames { get; }

    /// <summary>
    /// Every command this executor registered, with its aliases, flags, group and help text.
    /// HELP, autocomplete and the coverage report all read this rather than keeping a list.
    /// </summary>
    IReadOnlyCollection<CommandDescriptor> Commands => [];

    /// <summary>Looks up a command by global name or alias.</summary>
    bool TryGetCommand(string name, out CommandDescriptor command)
    {
        command = null!;
        return false;
    }

    /// <summary>True while a modal command on this executor is mid-prompt.</summary>
    bool HasActiveCommand { get; }

    /// <summary>
    /// Keyword/input policy of the prompt currently awaiting a response, or null when no
    /// command is prompting. UIs use it to render clickable keywords and to decide whether
    /// Space submits.
    /// </summary>
    PromptOptions? ActiveOptions => null;

    /// <summary>
    /// System variables this executor's target names. Autocomplete offers them the way they
    /// have to be typed — as an argument to SETVAR — since a bare variable name is not a
    /// command the runtime could run.
    /// </summary>
    IReadOnlyCollection<SystemVariable> Variables => [];

    bool IsTransparentCommand(string name);

    /// <summary>
    /// The global command name a registered name belongs to, or "" when the name is unknown.
    /// A name that differs from its global name is an alias, which the command line shows as
    /// "alias of ZOOM" and ranks below full command names.
    /// </summary>
    string ResolveGlobalName(string name) => "";

    /// <summary>The prompt currently awaiting an answer, or null. Shells inspect its type to
    /// answer a file prompt with a native dialog, or a point prompt with a viewport pick.</summary>
    PromptStep? ActiveStep => null;

    /// <summary>True while a command is waiting for a point the user can pick in the viewport.</summary>
    bool AwaitsPoint => false;

    /// <summary>The pending point prompt a viewport pick would answer, or null.</summary>
    PromptStep? PointPrompt => null;

    /// <summary>Answers a pending point prompt with a viewport pick.</summary>
    CommandResult SupplyPoint(OpenTK.Mathematics.Vector3 world, int screenX, int screenY) => CommandResult.End();

    /// <summary>Cancels the executor's active command, if any.</summary>
    CommandResult CancelActive();

    CommandResult Execute(string input);
}
