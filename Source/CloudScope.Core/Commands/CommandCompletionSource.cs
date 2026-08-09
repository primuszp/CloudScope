namespace CloudScope.Commands;

public enum CompletionKind
{
    /// <summary>A keyword of the prompt currently awaiting a response.</summary>
    Keyword,

    /// <summary>A command's global name.</summary>
    Command,

    /// <summary>An alias of a command.</summary>
    Alias
}

/// <summary>
/// One autocomplete candidate. <see cref="Insert"/> is what replaces the typed text,
/// <see cref="Display"/> is what the list shows (for keywords the capitalised letters
/// mark the accepted abbreviation).
/// </summary>
public sealed record CommandCompletion(CompletionKind Kind, string Insert, string Display, string Detail = "");

/// <summary>
/// Ranks completions for what the user has typed. Keywords of the active prompt come
/// first (they are the only valid answers there), then command names, then aliases;
/// within a group, prefix matches beat contains-matches and shorter beats longer.
/// </summary>
public static class CommandCompletionSource
{
    public static IReadOnlyList<CommandCompletion> Complete(
        string prefix,
        PromptOptions? activeOptions,
        IReadOnlyCollection<string>? knownCommandNames,
        Func<string, string>? resolveGlobalName = null,
        int limit = 12)
    {
        string typed = prefix.Trim();
        if (typed.Length == 0)
            return [];

        var results = new List<(int Rank, int Length, CommandCompletion Completion)>();

        foreach (Keyword keyword in activeOptions?.Keywords ?? [])
        {
            int rank = Match(keyword.GlobalName, typed);
            if (rank < 0 && keyword.Abbreviation.Length > 0)
                rank = Match(keyword.Abbreviation, typed);
            if (rank < 0)
                continue;

            string detail = keyword.Abbreviation.Length > 0 ? $"keyword · type {keyword.Abbreviation}" : "keyword";
            results.Add((rank, keyword.Display.Length,
                new CommandCompletion(CompletionKind.Keyword, keyword.GlobalName, keyword.Display, detail)));
        }

        // Rank by how well the text matches first, and only then by what the candidate is:
        // a command whose name starts with the typed text always beats an unrelated command
        // that merely contains it.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in knownCommandNames ?? [])
        {
            if (!seen.Add(name))
                continue;

            int match = Match(name, typed);
            if (match < 0)
                continue;

            string globalName = resolveGlobalName?.Invoke(name) ?? "";
            bool isAlias = globalName.Length > 0 && !globalName.Equals(name, StringComparison.OrdinalIgnoreCase);

            results.Add((CommandRank + (isAlias ? AliasRank : 0) + match, name.Length,
                new CommandCompletion(
                    isAlias ? CompletionKind.Alias : CompletionKind.Command,
                    name,
                    name,
                    isAlias ? $"alias of {globalName}" : "command")));
        }

        return results
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Length)
            .ThenBy(entry => entry.Completion.Display, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(entry => entry.Completion)
            .ToArray();
    }

    // How well the text matches dominates everything: every prefix match is listed before
    // any mid-word match, because someone typing "z" wants ZOOM, not POINTSIZE. Within a
    // match quality, prompt keywords (0) beat commands (100), which beat aliases (150).
    private const int CommandRank = 100;
    private const int AliasRank = 50;
    private const int ContainsPenalty = 1000;

    private static int Match(string candidate, string typed)
    {
        if (candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return 0;
        if (candidate.Contains(typed, StringComparison.OrdinalIgnoreCase)) return ContainsPenalty;
        return -1;
    }
}
