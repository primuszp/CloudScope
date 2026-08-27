namespace CloudScope.Commands;

/// <summary>
/// A command surface that can produce output without the shell having submitted anything —
/// a viewport pick answering a point prompt. The shell subscribes so a picked point is
/// echoed on the command line exactly like a typed one.
/// </summary>
public interface ICommandOutputSource
{
    event Action<CommandResult>? OutputProduced;
}
