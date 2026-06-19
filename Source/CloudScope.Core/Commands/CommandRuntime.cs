using System.Reflection;

namespace CloudScope.Commands;

public enum CommandStatus { Ended, Prompting, Cancelled, Failed }

public sealed record CommandResult(CommandStatus Status, string Message = "", string Prompt = "Command:")
{
    /// <summary>Keyword/input policy for a prompting result; null for non-prompting results.</summary>
    public PromptOptions? Options { get; init; }

    public static CommandResult End(string message = "") => new(CommandStatus.Ended, message);
    public static CommandResult Continue(string prompt, string message = "") => new(CommandStatus.Prompting, message, prompt);
    public static CommandResult Continue(PromptOptions options, string message = "") =>
        new(CommandStatus.Prompting, message, options.Message) { Options = options };
    public static CommandResult Cancel(string message = "*Cancel*") => new(CommandStatus.Cancelled, message);
}

public sealed class CommandEventArgs(string globalCommandName) : EventArgs
{
    public string GlobalCommandName { get; } = globalCommandName;
}

public sealed class CommandContext(object target)
{
    public object Target { get; } = target;

    /// <summary>The raw response text for the current prompt (free-form data such as points or value lists).</summary>
    public string Input { get; internal set; } = "";

    /// <summary>
    /// The global name of the keyword the runtime resolved from <see cref="Input"/> against the active
    /// prompt's <see cref="PromptOptions"/>, or "" when no keyword applied (free-form input or first invocation).
    /// </summary>
    public string Keyword { get; internal set; } = "";

    public T GetTarget<T>() where T : class => Target as T
        ?? throw new InvalidOperationException($"Command target is not {typeof(T).Name}.");
}

public interface ICommandCancellationHandler
{
    void CancelCommand(CommandContext context, string globalCommandName);
}

public sealed class CommandRuntime : ICommandExecutor
{
    private readonly CommandContext _context;
    private readonly Dictionary<string, Descriptor> _commands = new(StringComparer.OrdinalIgnoreCase);
    private Descriptor? _active;
    private PromptOptions? _activePrompt;

    public CommandRuntime(object target, params object[] commandClasses)
    {
        _context = new CommandContext(target);
        foreach (object commandClass in commandClasses) Register(commandClass);
    }

    public event EventHandler<CommandEventArgs>? CommandStarted;
    public event EventHandler<CommandEventArgs>? CommandEnded;
    public event EventHandler<CommandEventArgs>? CommandCancelled;
    public event EventHandler<CommandEventArgs>? CommandFailed;

    public string CurrentPrompt { get; private set; } = "Command:";
    public string? LastCompletedCommand { get; private set; }

    public bool IsKnownCommand(string name) => _commands.ContainsKey(name);

    public CommandResult Execute(string input)
    {
        string trimmed = input.Trim();

        if (_active != null)
        {
            if (IsCancel(trimmed))
                return CancelActive();

            // A keyword (or the default keyword on empty input) of the active prompt always
            // wins over command switching. This is what keeps a value like "Z 1 2" at a filter
            // prompt from being hijacked by the ZOOM alias "Z".
            string? keyword = _activePrompt?.Resolve(trimmed);
            if (keyword != null)
                return InvokeActive(trimmed, keyword);

            // Not a keyword. At a free-form (value/point) prompt the text is data for the active
            // command. Only at a keyword-only prompt does a known command name start a new command
            // (AutoCAD-style: menus and toolbars issue commands this way).
            bool allowArbitrary = _activePrompt?.AllowArbitraryInput ?? false;
            if (!allowArbitrary)
            {
                string[] parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && _commands.TryGetValue(parts[0], out Descriptor? incoming))
                {
                    string incomingInput = parts.Length > 1 ? parts[1] : "";
                    if (incoming.Flags.HasFlag(CommandFlags.Transparent))
                        return RunTransparent(incoming, incomingInput);
                    CancelActive();
                    return ActivateAndRun(incoming, incomingInput);
                }
            }

            return InvokeActive(trimmed, "");
        }

        if (trimmed.Length == 0)
        {
            if (LastCompletedCommand == null) return CommandResult.End();
            trimmed = LastCompletedCommand;
        }

        string[] cmdParts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!_commands.TryGetValue(cmdParts[0], out Descriptor? descriptor))
            return CommandResult.End($"Unknown command \"{cmdParts[0]}\". Press F1 or type HELP.");

        return ActivateAndRun(descriptor, cmdParts.Length > 1 ? cmdParts[1] : "");
    }

    public CommandResult CancelActive()
    {
        if (_active == null) return CommandResult.Cancel();
        Descriptor descriptor = _active;
        ResetActive(descriptor);
        CommandCancelled?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return CommandResult.Cancel();
    }

    public bool HasActiveCommand => _active != null;

    public bool IsTransparentCommand(string name) =>
        _commands.TryGetValue(name, out Descriptor? descriptor) && descriptor.Flags.HasFlag(CommandFlags.Transparent);

    public IReadOnlyCollection<string> KnownCommandNames => _commands.Keys;

    private CommandResult ActivateAndRun(Descriptor descriptor, string input)
    {
        _active = descriptor;
        _activePrompt = null;
        CommandStarted?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return InvokeActive(input, "");
    }

    private CommandResult RunTransparent(Descriptor descriptor, string input)
    {
        _context.Input = input;
        _context.Keyword = "";
        CommandStarted?.Invoke(this, new CommandEventArgs(descriptor.Name));
        try
        {
            var result = (CommandResult?)descriptor.Method.Invoke(descriptor.Target, [_context]) ?? CommandResult.End();
            if (!descriptor.Flags.HasFlag(CommandFlags.NoHistory))
                LastCompletedCommand = descriptor.Name;
            CommandEnded?.Invoke(this, new CommandEventArgs(descriptor.Name));
            return result;
        }
        catch (Exception ex)
        {
            CommandFailed?.Invoke(this, new CommandEventArgs(descriptor.Name));
            return new CommandResult(CommandStatus.Failed, $"Command failed: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    private CommandResult InvokeActive(string input, string keyword)
    {
        Descriptor descriptor = _active!;
        _context.Input = input;
        _context.Keyword = keyword;
        try
        {
            var result = (CommandResult?)descriptor.Method.Invoke(descriptor.Target, [_context]) ?? CommandResult.End();
            CurrentPrompt = result.Prompt;
            if (result.Status == CommandStatus.Prompting)
            {
                _activePrompt = result.Options;
                return result;
            }

            if (result.Status == CommandStatus.Cancelled)
            {
                ResetActive(descriptor);
                CommandCancelled?.Invoke(this, new CommandEventArgs(descriptor.Name));
            }
            else
            {
                ResetActive(descriptor: null);
                if (!descriptor.Flags.HasFlag(CommandFlags.NoHistory)) LastCompletedCommand = descriptor.Name;
                CommandEnded?.Invoke(this, new CommandEventArgs(descriptor.Name));
            }
            return result;
        }
        catch (Exception ex)
        {
            // A failing command must not leave its transient prompt state stranded, so give the
            // command class a chance to reset before clearing the runtime's active command.
            ResetActive(descriptor);
            CommandFailed?.Invoke(this, new CommandEventArgs(descriptor.Name));
            return new CommandResult(CommandStatus.Failed, $"Command failed: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    // When a descriptor is supplied, notify its cancellation handler so the command can roll back
    // any transient phase state it keeps. Always clear the runtime's active command and prompt.
    private void ResetActive(Descriptor? descriptor)
    {
        if (descriptor?.Target is ICommandCancellationHandler handler)
            handler.CancelCommand(_context, descriptor.Name);
        _active = null;
        _activePrompt = null;
        CurrentPrompt = "Command:";
    }

    private void Register(object target)
    {
        foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        foreach (CommandMethodAttribute attribute in method.GetCustomAttributes<CommandMethodAttribute>())
        {
            ValidateSignature(method);
            var descriptor = new Descriptor(attribute.GlobalName.ToUpperInvariant(), attribute.Flags, target, method);
            AddName(descriptor.Name, descriptor);
            foreach (string alias in attribute.Aliases) AddName(alias.ToUpperInvariant(), descriptor);
        }
    }

    private void AddName(string name, Descriptor descriptor)
    {
        if (!_commands.TryAdd(name, descriptor))
            throw new InvalidOperationException($"Command name or alias '{name}' is already registered.");
    }

    private static void ValidateSignature(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (method.ReturnType != typeof(CommandResult) || parameters.Length != 1 ||
            parameters[0].ParameterType != typeof(CommandContext))
            throw new InvalidOperationException($"{method.DeclaringType?.Name}.{method.Name} must return CommandResult and accept one CommandContext.");
    }

    private static bool IsCancel(string s) =>
        s.Equals("CANCEL", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("ESC",    StringComparison.OrdinalIgnoreCase) ||
        s.Equals("ESCAPE", StringComparison.OrdinalIgnoreCase);

    private sealed record Descriptor(string Name, CommandFlags Flags, object Target, MethodInfo Method);
}
