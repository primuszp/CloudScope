using System.Globalization;
using OpenTK.Mathematics;

namespace CloudScope.Commands;

/// <summary>Outcome of one prompt, mirroring AutoCAD's <c>PromptStatus</c>.</summary>
public enum PromptStatus
{
    /// <summary>The user supplied a value.</summary>
    OK,

    /// <summary>The user answered with one of the prompt's keywords.</summary>
    Keyword,

    /// <summary>The user cancelled (Escape).</summary>
    Cancel,

    /// <summary>The user pressed Enter and the prompt had no default.</summary>
    None
}

/// <summary>
/// One question a command asks. A command body yields the step and reads its result on the
/// next line, which is what lets multi-step commands be written as linear code instead of a
/// hand-rolled state machine:
/// <code>
/// var p = ed.GetPoint("Specify base point:");
/// yield return p;
/// if (!p.IsOk) yield break;
/// </code>
/// </summary>
public abstract class PromptStep
{
    private PromptOptions? _options;
    private readonly List<Keyword> _keywords = [];

    protected PromptStep(string message, bool allowArbitraryInput)
    {
        Message = message;
        AllowArbitraryInput = allowArbitraryInput;
    }

    public string Message { get; }
    public bool AllowArbitraryInput { get; }
    public string? DefaultKeyword { get; private set; }

    /// <summary>
    /// True when the step takes the whole remainder of the command line rather than one
    /// token — file paths and value lists ("2 3 4") need this.
    /// </summary>
    public bool ConsumesRest { get; private set; }

    public PromptStatus Status { get; internal set; } = PromptStatus.None;

    /// <summary>Global name of the keyword the user answered with, or "".</summary>
    public string Keyword { get; internal set; } = "";

    /// <summary>Raw text the user typed.</summary>
    public string Text { get; internal set; } = "";

    /// <summary>Re-prompt message set by a failed <see cref="Accept"/>.</summary>
    internal string Error { get; set; } = "";

    public bool IsOk => Status == PromptStatus.OK;
    public bool IsCancelled => Status == PromptStatus.Cancel;

    /// <summary>True when the user answered with the named keyword.</summary>
    public bool Is(string globalKeyword) =>
        Status == PromptStatus.Keyword && Keyword.Equals(globalKeyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the step produced neither a value nor a keyword.</summary>
    public bool IsEmpty => Status is PromptStatus.None or PromptStatus.Cancel;

    public PromptOptions Options => _options ??= new PromptOptions(Message, _keywords.ToArray())
    {
        DefaultKeyword = DefaultKeyword,
        AllowArbitraryInput = AllowArbitraryInput
    };

    /// <summary>Parses a typed answer. Returns false — with <see cref="Error"/> set — to re-ask.</summary>
    internal abstract bool Accept(string input);

    /// <summary>True when Enter alone answers the prompt with a value.</summary>
    internal virtual bool ApplyDefaultValue() => false;

    internal void AddKeywords(IEnumerable<Keyword> keywords)
    {
        _keywords.AddRange(keywords);
        _options = null;
    }

    internal void SetDefaultKeyword(string globalName)
    {
        DefaultKeyword = globalName.ToUpperInvariant();
        _options = null;
    }

    internal void SetConsumesRest() => ConsumesRest = true;

    internal void Reset()
    {
        Status = PromptStatus.None;
        Keyword = "";
        Text = "";
        Error = "";
    }
}

/// <summary>Fluent configuration shared by every step type.</summary>
public static class PromptStepExtensions
{
    public static TStep WithKeywords<TStep>(this TStep step, params Keyword[] keywords) where TStep : PromptStep
    {
        step.AddKeywords(keywords);
        return step;
    }

    public static TStep WithDefaultKeyword<TStep>(this TStep step, string globalName) where TStep : PromptStep
    {
        step.SetDefaultKeyword(globalName);
        return step;
    }

    /// <summary>Takes the rest of the command line, for paths and value lists.</summary>
    public static TStep TakingRest<TStep>(this TStep step) where TStep : PromptStep
    {
        step.SetConsumesRest();
        return step;
    }
}
