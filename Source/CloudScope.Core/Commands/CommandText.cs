namespace CloudScope.Commands;

public static class CommandText
{
    public static string FirstWord(string commandText)
    {
        string trimmed = commandText.Trim();
        int end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end])) end++;
        return trimmed[..end];
    }

    public static bool TryMatch(string commandText, string commandName, out string argument)
    {
        argument = "";
        string trimmed = commandText.Trim();
        string first = FirstWord(trimmed);
        if (!first.Equals(commandName, StringComparison.OrdinalIgnoreCase)) return false;
        argument = Unquote(trimmed[first.Length..]);
        return true;
    }

    public static string Unquote(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }
}
