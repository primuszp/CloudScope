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
    /// <summary>How submitted input is written into the transcript.</summary>
    public const string EchoPrefix = "Command: ";

    /// <summary>
    /// The command this line echoed, or null when it is not an echo. A UI offers it back to
    /// the user — AutoCAD recalls a command by double-clicking it in the history — without
    /// having to know how the echo was spelled.
    /// </summary>
    public string? EchoedCommand
    {
        get
        {
            if (Kind != CommandEntryKind.Echo || !Text.StartsWith(EchoPrefix, StringComparison.Ordinal))
                return null;

            string command = Text[EchoPrefix.Length..].Trim();
            return command.Length == 0 || command == "<repeat>" ? null : command;
        }
    }

    public override string ToString() => Text;
}
