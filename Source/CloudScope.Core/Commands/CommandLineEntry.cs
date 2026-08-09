namespace CloudScope.Commands;

/// <summary>
/// Role of a command-line history line. UIs style each kind differently
/// (echoed input dim, errors red, prompts accented) instead of parsing text.
/// </summary>
public enum CommandEntryKind
{
    /// <summary>Startup/informational banner.</summary>
    Banner,

    /// <summary>The input the user submitted, echoed back as "Command: X".</summary>
    Echo,

    /// <summary>A prompt line issued by a command that is still running.</summary>
    Prompt,

    /// <summary>Normal command output.</summary>
    Output,

    /// <summary>A failed or cancelled command's message.</summary>
    Error
}

/// <summary>One line of command-line history together with its role.</summary>
public sealed record CommandLineEntry(CommandEntryKind Kind, string Text)
{
    public override string ToString() => Text;
}
