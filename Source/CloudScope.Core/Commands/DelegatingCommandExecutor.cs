namespace CloudScope.Commands;

/// <summary>
/// An <see cref="ICommandExecutor"/> that resolves its target on every call, and reports a
/// "not ready" result while there is none.
/// </summary>
/// <remarks>
/// Hosts create their command surface asynchronously — a native view exists before its
/// renderer and command runtime do. Without this, every layer between the command line and
/// the runtime re-declares the same nine members just to null-check and forward them.
/// </remarks>
public sealed class DelegatingCommandExecutor : ICommandExecutor
{
    private readonly Func<ICommandExecutor?> _resolve;
    private readonly string _notReadyMessage;
    private readonly Action? _afterExecute;

    /// <param name="resolve">Returns the current target, or null while it does not exist.</param>
    /// <param name="notReadyMessage">Message reported when no target exists.</param>
    /// <param name="afterExecute">
    /// Invoked after a command runs — hosts that render on demand use it to request a redraw.
    /// </param>
    public DelegatingCommandExecutor(Func<ICommandExecutor?> resolve, string notReadyMessage, Action? afterExecute = null)
    {
        _resolve = resolve;
        _notReadyMessage = notReadyMessage;
        _afterExecute = afterExecute;
    }

    public string CurrentPrompt => _resolve()?.CurrentPrompt ?? "Command:";

    public PromptOptions? ActiveOptions => _resolve()?.ActiveOptions;

    public bool IsKnownCommand(string name) => _resolve()?.IsKnownCommand(name) == true;

    public IReadOnlyCollection<string> KnownCommandNames => _resolve()?.KnownCommandNames ?? [];

    public IReadOnlyCollection<CommandDescriptor> Commands => _resolve()?.Commands ?? [];

    public bool TryGetCommand(string name, out CommandDescriptor command)
    {
        ICommandExecutor? target = _resolve();
        if (target != null)
            return target.TryGetCommand(name, out command);

        command = null!;
        return false;
    }

    public bool HasActiveCommand => _resolve()?.HasActiveCommand == true;

    public bool IsTransparentCommand(string name) => _resolve()?.IsTransparentCommand(name) == true;

    public string ResolveGlobalName(string name) => _resolve()?.ResolveGlobalName(name) ?? "";

    public PromptStep? ActiveStep => _resolve()?.ActiveStep;

    public bool AwaitsPoint => _resolve()?.AwaitsPoint == true;

    public PromptStep? PointPrompt => _resolve()?.PointPrompt;

    public CommandResult SupplyPoint(OpenTK.Mathematics.Vector3 world, int screenX, int screenY)
    {
        CommandResult result = _resolve()?.SupplyPoint(world, screenX, screenY) ?? CommandResult.End();
        _afterExecute?.Invoke();
        return result;
    }

    public CommandResult CancelActive()
    {
        CommandResult result = _resolve()?.CancelActive() ?? CommandResult.Cancel();
        _afterExecute?.Invoke();
        return result;
    }

    public CommandResult Execute(string input)
    {
        ICommandExecutor? target = _resolve();
        if (target == null)
            return CommandResult.End(_notReadyMessage);

        CommandResult result = target.Execute(input);
        _afterExecute?.Invoke();
        return result;
    }
}
