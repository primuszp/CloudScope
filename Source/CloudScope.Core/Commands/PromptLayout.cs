namespace CloudScope.Commands;

public enum PromptSegmentKind
{
    /// <summary>Literal prompt text, drawn as written.</summary>
    Text,

    /// <summary>A keyword of the active prompt, drawn as something the user can click.</summary>
    Keyword
}

/// <summary>
/// One piece of a laid-out prompt line. A keyword segment carries the
/// <see cref="Commands.Keyword"/> it stands for, so a shell can underline the accepted
/// abbreviation and submit the global name when it is clicked.
/// </summary>
public sealed record PromptSegment(PromptSegmentKind Kind, string Text, Keyword? Keyword = null, bool IsDefault = false);

/// <summary>
/// Splits a prompt line into text and keywords, so the keywords are clickable where they are
/// written — inside the "[Box/Cylinder/Sphere]" of the prompt itself. Both shells lay the
/// prompt out through this, rather than each parsing the brackets its own way or, as they did,
/// stacking a separate row of keyword buttons above a prompt that already lists them.
/// </summary>
public static class PromptLayout
{
    public static IReadOnlyList<PromptSegment> Split(string message, PromptOptions? options)
    {
        IReadOnlyList<Keyword> keywords = options?.Keywords ?? [];
        if (keywords.Count == 0)
            return [new PromptSegment(PromptSegmentKind.Text, message)];

        int open = message.IndexOf('[');
        int close = open < 0 ? -1 : message.IndexOf(']', open + 1);

        // A prompt that lists no keywords still has them; they are appended in the notation
        // the user would have read, so clicking and typing stay the same thing.
        if (close < 0)
            return Bracketed(message + " ", "", keywords, options);

        // The tail starts at the bracket, not after it: the head owns the "[" and the tail owns
        // the "]" and everything the prompt writes past it, such as its "<Box>:" default.
        return Bracketed(message[..(open + 1)], message[close..],
            SplitTokens(message[(open + 1)..close]), keywords, options);
    }

    private static IReadOnlyList<PromptSegment> Bracketed(
        string head, string tail, IReadOnlyList<Keyword> keywords, PromptOptions? options)
    {
        var segments = new List<PromptSegment> { new(PromptSegmentKind.Text, head + "[") };
        for (int i = 0; i < keywords.Count; i++)
        {
            if (i > 0)
                segments.Add(new PromptSegment(PromptSegmentKind.Text, "/"));

            segments.Add(KeywordSegment(keywords[i].Display, keywords[i], options));
        }

        segments.Add(new PromptSegment(PromptSegmentKind.Text, "]" + tail));
        return segments;
    }

    private static IReadOnlyList<PromptSegment> Bracketed(
        string head, string tail, IReadOnlyList<string> tokens,
        IReadOnlyList<Keyword> keywords, PromptOptions? options)
    {
        var segments = new List<PromptSegment> { new(PromptSegmentKind.Text, head) };
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
                segments.Add(new PromptSegment(PromptSegmentKind.Text, "/"));

            string token = tokens[i];
            Keyword? match = Find(token, keywords);

            // A bracketed word the prompt's keyword list does not know is left as text: it is
            // notation like "<0-9>", not something clicking could answer.
            segments.Add(match == null
                ? new PromptSegment(PromptSegmentKind.Text, token)
                : KeywordSegment(token, match, options));
        }

        segments.Add(new PromptSegment(PromptSegmentKind.Text, tail));
        return segments;
    }

    private static PromptSegment KeywordSegment(string text, Keyword keyword, PromptOptions? options) =>
        new(PromptSegmentKind.Keyword, text, keyword,
            string.Equals(keyword.GlobalName, options?.DefaultKeyword, StringComparison.OrdinalIgnoreCase));

    private static Keyword? Find(string token, IReadOnlyList<Keyword> keywords)
    {
        foreach (Keyword keyword in keywords)
            if (keyword.Matches(token) || keyword.Display.Equals(token, StringComparison.OrdinalIgnoreCase))
                return keyword;

        return null;
    }

    private static string[] SplitTokens(string inside) =>
        inside.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
