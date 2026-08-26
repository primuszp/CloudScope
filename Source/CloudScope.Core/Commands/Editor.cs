using OpenTK.Mathematics;

namespace CloudScope.Commands;

/// <summary>
/// The command line as a command sees it: it writes messages here and asks its questions
/// here. Every question returns a <see cref="PromptStep"/> the command yields, so a command
/// body reads top to bottom in the order the user is asked.
/// </summary>
/// <remarks>
/// This is the single entry point for command input. Menus, toolbars, shortcuts, scripts and
/// viewport picks all arrive as answers to the step a command is currently waiting on, which
/// is what removes the per-command phase fields the old runtime needed.
/// </remarks>
public sealed class Editor
{
    private readonly List<string> _messages = [];

    /// <summary>Set by <see cref="Cancel"/>; makes the command report "*Cancel*" when it ends.</summary>
    internal bool CancelRequested { get; private set; }

    /// <summary>
    /// True when the command was invoked with arguments on the command line. A few commands
    /// behave differently when simply repeated with Enter (SELECT accepts the active
    /// selection), which is the same distinction AutoCAD draws.
    /// </summary>
    public bool HasArguments { get; internal set; }

    /// <summary>Writes one line of command-line output.</summary>
    public void WriteMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _messages.Add(message);
    }

    /// <summary>Ends the command as cancelled once it returns.</summary>
    public void Cancel(string message = "*Cancel*")
    {
        CancelRequested = true;
        WriteMessage(message);
    }

    private readonly Queue<string> _deferred = new();

    /// <summary>
    /// Queues a command line to run once this command has finished. SCRIPT is built on it:
    /// a command cannot start another while it is itself active, so it asks the runtime to
    /// run the lines after it returns.
    /// </summary>
    public void RunAfter(string commandText)
    {
        if (!string.IsNullOrWhiteSpace(commandText))
            _deferred.Enqueue(commandText);
    }

    internal bool HasDeferred => _deferred.Count > 0;

    internal string TakeDeferred() => _deferred.Dequeue();

    internal string DrainMessages()
    {
        if (_messages.Count == 0) return "";
        string text = string.Join(Environment.NewLine, _messages);
        _messages.Clear();
        return text;
    }

    internal void Reset()
    {
        _messages.Clear();
        CancelRequested = false;
        HasArguments = false;
        _deferred.Clear();
    }

    // ----- Prompts -----

    public PromptKeywordStep GetKeywords(string message, params Keyword[] keywords) =>
        new PromptKeywordStep(message).WithKeywords(keywords);

    public PromptStringStep GetString(string message) => new(message);

    /// <summary>A text prompt that takes the whole rest of the line, spaces included.</summary>
    public PromptStringStep GetLine(string message) => new PromptStringStep(message).TakingRest();

    public PromptIntegerStep GetInteger(string message) => new(message);

    public PromptDoubleStep GetDouble(string message) => new(message);

    /// <summary>A non-negative length.</summary>
    public PromptDoubleStep GetDistance(string message) => new PromptDoubleStep(message).WithRange(0, double.MaxValue);

    /// <summary>An angle in degrees.</summary>
    public PromptDoubleStep GetAngle(string message) => new(message);

    public PromptPointStep GetPoint(string message) => new(message);

    /// <summary>The opposite corner of a window, rubber-banded from <paramref name="basePoint"/>.</summary>
    public PromptPointStep GetCorner(string message, Vector3 basePoint) => new PromptPointStep(message).From(basePoint);

    public PromptScreenPointStep GetScreenPoint(string message) => new(message);

    public PromptFileStep GetFileNameForOpen(string message) =>
        new PromptFileStep(message, PromptFileMode.OpenFile).TakingRest();

    public PromptFileStep GetFileNameForSave(string message) =>
        new PromptFileStep(message, PromptFileMode.SaveFile).TakingRest();

    public PromptFileStep GetDirectory(string message) =>
        new PromptFileStep(message, PromptFileMode.Directory).TakingRest();
}
