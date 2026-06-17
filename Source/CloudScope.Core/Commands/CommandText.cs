namespace CloudScope.Commands;

public static class CommandText
{
    public static string FirstWord(string commandText)
    {
        string trimmed = commandText.Trim();
        return trimmed.Length == 0
            ? ""
            : trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    }

    public static bool TryMatch(string commandText, string commandName, out string argument)
    {
        argument = "";
        string trimmed = commandText.Trim();
        if (trimmed.Length == 0)
            return false;

        string[] parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!parts[0].Equals(commandName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (parts.Length == 2)
            argument = Unquote(parts[1]);
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
