namespace CloudScope.Commands;

/// <summary>
/// Describes the keywords and input policy of an active prompt, mirroring the role
/// of AutoCAD's <c>PromptKeywordOptions</c>. The runtime uses this to resolve
/// keyword input, apply the default keyword on an empty response, and decide
/// whether a typed token is data for the active command or a new command name.
/// </summary>
public sealed class PromptOptions
{
    public PromptOptions(string message, params Keyword[] keywords)
    {
        Message = message;
        Keywords = keywords;
    }

    /// <summary>Full prompt line shown to the user (including any "[...]" list).</summary>
    public string Message { get; init; }

    public IReadOnlyList<Keyword> Keywords { get; init; }

    /// <summary>Global name of the keyword used when the user presses Enter with no text.</summary>
    public string? DefaultKeyword { get; init; }

    /// <summary>
    /// When true the prompt accepts free-form data (points, numbers, value lists) in
    /// addition to its keywords, so a typed token that is not a keyword is delivered
    /// to the active command rather than treated as a new command.
    /// </summary>
    public bool AllowArbitraryInput { get; init; }

    public string? Resolve(string input)
    {
        if (input.Trim().Length == 0)
            return DefaultKeyword;

        foreach (Keyword keyword in Keywords)
            if (keyword.Matches(input))
                return keyword.GlobalName;

        return null;
    }
}
