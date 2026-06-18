using System.Reflection;

namespace CloudScope.Commands;

public enum CommandStatus { Ended, Prompting, Cancelled, Failed }

public sealed record CommandResult(CommandStatus Status, string Message = "", string Prompt = "Command:")
{
    public static CommandResult End(string message = "") => new(CommandStatus.Ended, message);
    public static CommandResult Continue(string prompt, string message = "") => new(CommandStatus.Prompting, message, prompt);
    public static CommandResult Cancel(string message = "*Cancel*") => new(CommandStatus.Cancelled, message);
}

public sealed class CommandEventArgs(string globalCommandName) : EventArgs
{
    public string GlobalCommandName { get; } = globalCommandName;
}

public sealed class CommandContext(object target)
{
    public object Target { get; } = target;
    public string Input { get; internal set; } = "";
    public T GetTarget<T>() where T : class => Target as T
        ?? throw new InvalidOperationException($"Command target is not {typeof(T).Name}.");
}

public interface ICommandCancellationHandler
{
    void CancelCommand(CommandContext context, string globalCommandName);
}

public interface ICommandOptionProvider
{
    bool IsCommandOption(string globalCommandName, string input);
}

public sealed class CommandRuntime : ICommandExecutor
{
    private readonly CommandContext _context;
    private readonly Dictionary<string, Descriptor> _commands = new(StringComparer.OrdinalIgnoreCase);
    private Descriptor? _active;

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

            string[] parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && _commands.TryGetValue(parts[0], out Descriptor? incoming))
            {
                bool isOption = IsActiveOption(_active, trimmed);
                if (parts.Length == 2 || !isOption)
                {
                    string incomingInput = parts.Length > 1 ? parts[1] : "";
                    if (incoming.Flags.HasFlag(CommandFlags.Transparent))
                        return RunTransparent(incoming, incomingInput);
                    CancelActive();
                    return ActivateAndRun(incoming, incomingInput);
                }
            }

            return InvokeActive(trimmed);
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
        if (descriptor.Target is ICommandCancellationHandler handler)
            handler.CancelCommand(_context, descriptor.Name);
        _active = null;
        CurrentPrompt = "Command:";
        CommandCancelled?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return CommandResult.Cancel();
    }

    private CommandResult ActivateAndRun(Descriptor descriptor, string input)
    {
        _active = descriptor;
        _context.Input = input;
        CommandStarted?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return InvokeActive(input);
    }

    private CommandResult RunTransparent(Descriptor descriptor, string input)
    {
        _context.Input = input;
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

    private CommandResult InvokeActive(string input)
    {
        Descriptor descriptor = _active!;
        _context.Input = input;
        try
        {
            var result = (CommandResult?)descriptor.Method.Invoke(descriptor.Target, [_context]) ?? CommandResult.End();
            CurrentPrompt = result.Prompt;
            if (result.Status == CommandStatus.Prompting) return result;
            _active = null;
            CurrentPrompt = "Command:";
            if (result.Status == CommandStatus.Cancelled)
                CommandCancelled?.Invoke(this, new CommandEventArgs(descriptor.Name));
            else
            {
                if (!descriptor.Flags.HasFlag(CommandFlags.NoHistory)) LastCompletedCommand = descriptor.Name;
                CommandEnded?.Invoke(this, new CommandEventArgs(descriptor.Name));
            }
            return result;
        }
        catch (Exception ex)
        {
            _active = null;
            CurrentPrompt = "Command:";
            CommandFailed?.Invoke(this, new CommandEventArgs(descriptor.Name));
            return new CommandResult(CommandStatus.Failed, $"Command failed: {ex.InnerException?.Message ?? ex.Message}");
        }
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

    private static bool IsActiveOption(Descriptor descriptor, string input) =>
        descriptor.Target is ICommandOptionProvider provider &&
        provider.IsCommandOption(descriptor.Name, input);

    private sealed record Descriptor(string Name, CommandFlags Flags, object Target, MethodInfo Method);
}
