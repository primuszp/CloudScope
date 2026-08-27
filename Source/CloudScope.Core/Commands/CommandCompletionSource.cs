namespace CloudScope.Commands;

public enum CompletionKind
{
    /// <summary>A keyword of the prompt currently awaiting a response.</summary>
    Keyword,

    /// <summary>A command's global name.</summary>
    Command,

    /// <summary>An alias of a command.</summary>
    Alias,

    /// <summary>A system variable, offered as the command that reads and writes it.</summary>
    Variable
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
    /// <summary>
    /// The commands that take a system variable as their argument. Typing one of them and a
    /// partial name completes against the variable table rather than the command table, which
    /// is the only place a variable name is a valid thing to type.
    /// </summary>
    private static readonly string[] VariableCommands = ["SETVAR", "SET", "GETVAR"];

    public static IReadOnlyList<CommandCompletion> Complete(
        string prefix,
        PromptOptions? activeOptions,
        IReadOnlyCollection<string>? knownCommandNames,
        Func<string, string>? resolveGlobalName = null,
        int limit = 12,
        IReadOnlyCollection<SystemVariable>? variables = null,
        IReadOnlyCollection<string>? recentCommands = null)
    {
        string typed = prefix.Trim();
        if (typed.Length == 0)
            return [];

        // "SETVAR pt" is asking about variables, not commands.
        if (SplitVariableArgument(typed) is { } argument)
            return CompleteVariables(argument.Command, argument.Partial, variables, limit);

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

            results.Add((CommandRank + (isAlias ? AliasRank : 0) + match + RecentBonus(name, recentCommands),
                name.Length,
                new CommandCompletion(
                    isAlias ? CompletionKind.Alias : CompletionKind.Command,
                    name,
                    name,
                    isAlias ? $"alias of {globalName}" : "command")));
        }

        // A variable is offered as the command that would read it, because that is what the
        // user has to type: a bare variable name at the command prompt is not a command.
        foreach (SystemVariable variable in variables ?? [])
        {
            int match = Match(variable.Name, typed);
            if (match < 0)
                continue;

            results.Add((VariableRank + match, variable.Name.Length,
                new CommandCompletion(CompletionKind.Variable, $"SETVAR {variable.Name}", variable.Name,
                    $"variable · {variable.Description}")));
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
    private const int VariableRank = 125;
    private const int AliasRank = 50;
    private const int ContainsPenalty = 1000;

    // Recency reorders candidates within their group without ever promoting one past a better
    // match: what someone ran a minute ago is what they most likely want again.
    private static int RecentBonus(string name, IReadOnlyCollection<string>? recentCommands)
    {
        if (recentCommands == null)
            return 0;

        int index = 0;
        foreach (string recent in recentCommands)
        {
            if (recent.Equals(name, StringComparison.OrdinalIgnoreCase))
                return -(20 - Math.Min(index, 10));

            index++;
        }

        return 0;
    }

    private static (string Command, string Partial)? SplitVariableArgument(string typed)
    {
        int space = typed.IndexOf(' ');
        if (space < 0)
            return null;

        string command = typed[..space];
        foreach (string name in VariableCommands)
            if (command.Equals(name, StringComparison.OrdinalIgnoreCase))
                return (command.ToUpperInvariant(), typed[(space + 1)..].TrimStart());

        return null;
    }

    private static IReadOnlyList<CommandCompletion> CompleteVariables(
        string command, string partial, IReadOnlyCollection<SystemVariable>? variables, int limit) =>
        (variables ?? [])
            .Select(variable => (Rank: partial.Length == 0 ? 0 : Match(variable.Name, partial), Variable: variable))
            .Where(entry => entry.Rank >= 0)
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Variable.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(entry => new CommandCompletion(CompletionKind.Variable,
                $"{command} {entry.Variable.Name}", entry.Variable.Name,
                $"{entry.Variable.Value} · {entry.Variable.Description}"))
            .ToArray();

    private static int Match(string candidate, string typed)
    {
        if (candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return 0;
        if (candidate.Contains(typed, StringComparison.OrdinalIgnoreCase)) return ContainsPenalty;
        return -1;
    }
}
