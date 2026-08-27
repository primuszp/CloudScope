namespace CloudScope.Commands;

[Flags]
public enum CommandFlags
{
    None = 0,
    Transparent = 1,
    NoHistory = 2,
    NoUndoMarker = 4
}

/// <summary>
/// Where a command belongs in the command set. The group orders <c>HELP</c> and gives the
/// menu a name for the section a command lives in, so the two cannot describe the same
/// command differently.
/// </summary>
public enum CommandGroup
{
    File,
    Edit,
    Label,
    View,
    Inquiry,
    Settings,
    Utility
}

/// <summary>
/// What a command needs in order to run, mirroring the role of AutoCAD's session/document
/// command flags. The single command table uses it to explain why a command is unavailable
/// rather than reporting it as unknown.
/// </summary>
public enum CommandScope
{
    /// <summary>Always available — needs neither a viewer nor a loaded cloud.</summary>
    Application,

    /// <summary>Needs a viewer.</summary>
    Viewer,

    /// <summary>Needs a loaded point cloud.</summary>
    Document
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CommandMethodAttribute : Attribute
{
    public CommandMethodAttribute(string globalName, params string[] aliases)
    {
        GlobalName = globalName;
        Aliases = aliases;
    }

    public string GlobalName { get; }
    public IReadOnlyList<string> Aliases { get; }
    public CommandFlags Flags { get; init; }
    public CommandGroup Group { get; init; } = CommandGroup.Utility;
    public CommandScope Scope { get; init; } = CommandScope.Viewer;

    /// <summary>
    /// One line describing what the command does. Required: a command that cannot describe
    /// itself makes <c>HELP</c> depend on a hand-written list, which is what rots.
    /// </summary>
    public string Summary { get; init; } = "";

    /// <summary>Usage line, e.g. "ZOOM [All/Center/Extents/Object/Window/&lt;scale&gt;]".</summary>
    public string Syntax { get; init; } = "";
}
