namespace CloudScope.Commands;

[Flags]
public enum CommandFlags
{
    None = 0,
    Transparent = 1,
    NoHistory = 2,
    NoUndoMarker = 4
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
}
