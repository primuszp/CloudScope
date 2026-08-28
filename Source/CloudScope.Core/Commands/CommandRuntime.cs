using System.Reflection;
using OpenTK.Mathematics;

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

/// <summary>
/// What a command is given when it runs: the object it acts on and the command line it talks
/// to. A command body is <c>IEnumerable&lt;PromptStep&gt;</c>, so it keeps its own state in
/// locals between questions instead of in fields on the command class.
/// </summary>
public sealed class CommandContext(object target, Editor editor)
{
    public object Target { get; } = target;

    /// <summary>The command line: writes output and asks the command's questions.</summary>
    public Editor Editor { get; } = editor;

    public T GetTarget<T>() where T : class => Target as T
        ?? throw new InvalidOperationException($"Command target is not {typeof(T).Name}.");
}

public sealed class CommandRuntime : ICommandExecutor
{
    private readonly object _target;
    private readonly Editor _editor = new();
    private readonly CommandContext _context;
    private readonly Dictionary<string, Descriptor> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pending = new();

    private Descriptor? _active;
    private IEnumerator<PromptStep>? _steps;
    private PromptStep? _step;

    public CommandRuntime(object target, params object[] commandClasses)
    {
        _target = target;
        _context = new CommandContext(target, _editor);
        foreach (object commandClass in commandClasses) Register(commandClass);
    }

    public event EventHandler<CommandEventArgs>? CommandStarted;
    public event EventHandler<CommandEventArgs>? CommandEnded;
    public event EventHandler<CommandEventArgs>? CommandCancelled;
    public event EventHandler<CommandEventArgs>? CommandFailed;

    public string CurrentPrompt => _step?.Message ?? "Command:";
    public string? LastCompletedCommand { get; private set; }
    public bool HasActiveCommand => _active != null;
    public PromptOptions? ActiveOptions => _step?.Options;
    public bool IsKnownCommand(string name) => _commands.ContainsKey(name);
    public IReadOnlyCollection<string> KnownCommandNames => _commands.Keys;

    public IReadOnlyCollection<CommandDescriptor> Commands =>
        _commands.Values.Select(d => d.Info).Distinct().ToArray();

    public bool TryGetCommand(string name, out CommandDescriptor command)
    {
        if (_commands.TryGetValue(name, out Descriptor? descriptor))
        {
            command = descriptor.Info;
            return true;
        }

        command = null!;
        return false;
    }

    public string ResolveGlobalName(string name) =>
        _commands.TryGetValue(name, out Descriptor? descriptor) ? descriptor.Name : "";

    public bool IsTransparentCommand(string name) =>
        _commands.TryGetValue(name, out Descriptor? descriptor) && descriptor.Flags.HasFlag(CommandFlags.Transparent);

    /// <summary>The prompt currently awaiting an answer, or null.</summary>
    public PromptStep? ActiveStep => _step;

    /// <summary>True while a command is waiting for a point the user can pick in the viewport.</summary>
    public bool AwaitsPoint => _step is PromptPointStep or PromptScreenPointStep or PromptDistanceOrPointStep;

    /// <summary>The point prompt a viewport pick would answer, or null.</summary>
    public PromptStep? PointPrompt => AwaitsPoint ? _step : null;

    public CommandResult Execute(string input)
    {
        string trimmed = input.Trim();

        if (_active != null && _step != null)
        {
            if (IsCancel(trimmed))
                return CancelActive();

            // An apostrophe invokes a transparent command from inside another one, as it does
            // in AutoCAD ('ZOOM). It is the only unambiguous way in at a prompt that accepts
            // arbitrary text, where a bare name is data.
            if (trimmed.StartsWith('\''))
            {
                Queue<string> transparent = Tokenize(trimmed[1..]);
                if (transparent.Count == 0)
                    return Prompt();

                string transparentName = transparent.Dequeue();
                if (!_commands.TryGetValue(transparentName, out Descriptor? nested))
                    return Prompt($"Unknown command \"{transparentName}\".");

                if (!nested.Flags.HasFlag(CommandFlags.Transparent))
                    return Prompt($"{nested.Name} cannot be used transparently.");

                return RunTransparent(nested, transparent);
            }

            // A keyword (or the default keyword on empty input) of the active prompt always
            // wins over command switching. This is what keeps a value like "Z 1 2" at a filter
            // prompt from being hijacked by the ZOOM alias "Z".
            if (_step.Options.Resolve(trimmed) == null && !_step.AllowArbitraryInput)
            {
                // Not a keyword, and this prompt takes nothing else: at a keyword-only prompt a
                // known command name starts a new command, the way menus and toolbars issue them.
                Queue<string> tokens = Tokenize(trimmed);
                if (tokens.Count > 0 && _commands.TryGetValue(tokens.Peek(), out Descriptor? incoming))
                {
                    tokens.Dequeue();
                    if (incoming.Flags.HasFlag(CommandFlags.Transparent))
                        return RunTransparent(incoming, tokens);

                    CancelActive();
                    return Start(incoming, tokens);
                }
            }

            return Deliver(trimmed);
        }

        if (trimmed.Length == 0)
        {
            if (LastCompletedCommand == null) return CommandResult.End();
            trimmed = LastCompletedCommand;
        }

        if (trimmed.StartsWith('\''))
            trimmed = trimmed[1..].TrimStart();

        Queue<string> line = Tokenize(trimmed);
        if (line.Count == 0)
            return CommandResult.End();

        string name = line.Dequeue();
        if (!_commands.TryGetValue(name, out Descriptor? descriptor))
            return CommandResult.End($"Unknown command \"{name}\". Press F1 or type HELP.");

        return Start(descriptor, line);
    }

    /// <summary>
    /// Answers a pending point prompt with a viewport pick. This is the whole of what makes
    /// MOVE, ROTATE and ZOOM Window interactive: a click is an answer, not a separate path.
    /// </summary>
    public CommandResult SupplyPoint(Vector3 world, int screenX, int screenY)
    {
        switch (_step)
        {
            case PromptPointStep point:
                point.Value = world;
                point.Status = PromptStatus.OK;
                point.Text = $"{world.X:0.###},{world.Y:0.###},{world.Z:0.###}";
                return Advance();
            case PromptScreenPointStep screen:
                screen.X = screenX;
                screen.Y = screenY;
                screen.Status = PromptStatus.OK;
                screen.Text = $"{screenX},{screenY}";
                return Advance();
            case PromptDistanceOrPointStep distance:
                if (!distance.AcceptPick(world))
                    return Prompt();
                distance.Text = distance.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                return Advance();
            default:
                return CommandResult.End();
        }
    }

    public CommandResult CancelActive()
    {
        if (_active == null) return CommandResult.Cancel();
        Descriptor descriptor = _active;

        // Disposing the iterator resumes the command body at its yield and runs any finally
        // blocks, so a cancelled command unwinds its own state. Nothing has to be reset by name.
        ResetActive();
        CommandCancelled?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return CommandResult.Cancel();
    }

    // ----- Execution -----

    private CommandResult Start(Descriptor descriptor, Queue<string> arguments)
    {
        _active = descriptor;
        _step = null;
        _editor.Reset();
        _pending.Clear();
        foreach (string token in arguments) _pending.Enqueue(token);
        _editor.HasArguments = _pending.Count > 0;

        try
        {
            _steps = InvokeBody(descriptor, _context);
        }
        catch (Exception ex)
        {
            return Fail(descriptor, ex);
        }

        CommandStarted?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return Advance();
    }

    private CommandResult Deliver(string input)
    {
        if (!Satisfy(_step!, input))
            return Prompt();

        return Advance();
    }

    private CommandResult Advance()
    {
        Descriptor descriptor = _active!;
        try
        {
            while (_steps!.MoveNext())
            {
                _step = _steps.Current;
                _step.Reset();

                if (!TrySatisfyFromCommandLine(_step))
                    return Prompt();
            }
        }
        catch (Exception ex)
        {
            return Fail(descriptor, ex);
        }

        return Complete(descriptor);
    }

    /// <summary>
    /// Answers a step from what is left of the command line, so "ZOOM W 10,10 200,200" runs
    /// without stopping to ask. Returns false when the step has to be put to the user.
    /// </summary>
    private bool TrySatisfyFromCommandLine(PromptStep step)
    {
        if (_pending.Count == 0)
            return false;

        string text;
        if (step.ConsumesRest)
        {
            text = string.Join(' ', _pending);
            _pending.Clear();
        }
        else
        {
            text = _pending.Dequeue();
        }

        if (Satisfy(step, text))
            return true;

        // The typed line was wrong from here on; drop the rest rather than feeding it to
        // prompts it was never meant for.
        _pending.Clear();
        return false;
    }

    private static bool Satisfy(PromptStep step, string text)
    {
        step.Text = text;
        string trimmed = text.Trim();

        string? keyword = step.Options.Resolve(trimmed);
        if (keyword != null)
        {
            step.Status = PromptStatus.Keyword;
            step.Keyword = keyword;
            return true;
        }

        if (trimmed.Length == 0)
        {
            if (step.ApplyDefaultValue())
                return true;

            step.Status = PromptStatus.None;
            return true;
        }

        return step.Accept(trimmed);
    }

    private CommandResult Prompt(string note = "")
    {
        string message = Join(Join(_editor.DrainMessages(), _step!.Error), note);
        _step.Error = "";
        return CommandResult.Continue(_step.Options, message);
    }

    private bool _draining;

    private CommandResult Complete(Descriptor descriptor)
    {
        string message = _editor.DrainMessages();
        bool cancelled = _editor.CancelRequested;

        // Lines a command queued (SCRIPT) run once it has finished, so they start as ordinary
        // commands rather than being blocked by the command that asked for them.
        var deferred = new Queue<string>();
        while (_editor.HasDeferred) deferred.Enqueue(_editor.TakeDeferred());

        ResetActive();

        if (cancelled)
        {
            CommandCancelled?.Invoke(this, new CommandEventArgs(descriptor.Name));
            return CommandResult.Cancel(message.Length > 0 ? message : "*Cancel*");
        }

        if (!descriptor.Flags.HasFlag(CommandFlags.NoHistory))
            LastCompletedCommand = descriptor.Name;

        CommandEnded?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return RunDeferred(deferred, message);
    }

    private CommandResult RunDeferred(Queue<string> lines, string message)
    {
        if (lines.Count == 0 || _draining)
            return CommandResult.End(message);

        _draining = true;
        try
        {
            CommandResult last = CommandResult.End(message);
            while (lines.Count > 0)
            {
                CommandResult result = Execute(lines.Dequeue());
                message = Join(message, result.Message);
                last = result;

                // A line that failed or cancelled stops the rest, the way a script stops on
                // an error instead of running on against a state it did not expect.
                if (result.Status is CommandStatus.Failed or CommandStatus.Cancelled)
                    return result with { Message = message };
            }

            return last with { Message = message };
        }
        finally
        {
            _draining = false;
        }
    }

    private CommandResult Fail(Descriptor descriptor, Exception ex)
    {
        ResetActive();
        CommandFailed?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return new CommandResult(CommandStatus.Failed, $"Command failed: {ex.InnerException?.Message ?? ex.Message}");
    }

    /// <summary>
    /// Runs a transparent command to completion beside a suspended one. It gets its own editor
    /// and context so its output cannot mix with the interrupted command's.
    /// </summary>
    private CommandResult RunTransparent(Descriptor descriptor, Queue<string> arguments)
    {
        var editor = new Editor();
        var context = new CommandContext(_target, editor);
        CommandStarted?.Invoke(this, new CommandEventArgs(descriptor.Name));

        try
        {
            using IEnumerator<PromptStep> steps = InvokeBody(descriptor, context);
            while (steps.MoveNext())
            {
                PromptStep step = steps.Current;
                step.Reset();
                if (arguments.Count == 0)
                    return new CommandResult(CommandStatus.Failed,
                        $"{descriptor.Name} cannot ask questions while another command is active.");

                string text;
                if (step.ConsumesRest)
                {
                    text = string.Join(' ', arguments);
                    arguments.Clear();
                }
                else
                {
                    text = arguments.Dequeue();
                }

                if (!Satisfy(step, text))
                    return new CommandResult(CommandStatus.Failed, step.Error);
            }
        }
        catch (Exception ex)
        {
            CommandFailed?.Invoke(this, new CommandEventArgs(descriptor.Name));
            return new CommandResult(CommandStatus.Failed, $"Command failed: {ex.InnerException?.Message ?? ex.Message}");
        }

        if (!descriptor.Flags.HasFlag(CommandFlags.NoHistory))
            LastCompletedCommand = descriptor.Name;

        CommandEnded?.Invoke(this, new CommandEventArgs(descriptor.Name));
        return CommandResult.End(editor.DrainMessages());
    }

    private static IEnumerator<PromptStep> InvokeBody(Descriptor descriptor, CommandContext context)
    {
        var body = (IEnumerable<PromptStep>?)descriptor.Method.Invoke(descriptor.Target, [context]);
        return (body ?? []).GetEnumerator();
    }

    private void ResetActive()
    {
        _steps?.Dispose();
        _steps = null;
        _step = null;
        _active = null;
        _pending.Clear();
        _editor.Reset();
    }

    // ----- Registration -----

    private void Register(object target)
    {
        foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        foreach (CommandMethodAttribute attribute in method.GetCustomAttributes<CommandMethodAttribute>())
        {
            ValidateSignature(method);
            ValidateMetadata(method, attribute);
            var info = new CommandDescriptor(
                attribute.GlobalName.ToUpperInvariant(),
                attribute.Aliases.Select(a => a.ToUpperInvariant()).ToArray(),
                attribute.Flags,
                attribute.Group,
                attribute.Scope,
                attribute.Summary,
                attribute.Syntax) { Implementation = method };
            var descriptor = new Descriptor(info, target, method);
            AddName(descriptor.Name, descriptor);
            foreach (string alias in info.Aliases) AddName(alias, descriptor);
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
        if (method.ReturnType != typeof(IEnumerable<PromptStep>) || parameters.Length != 1 ||
            parameters[0].ParameterType != typeof(CommandContext))
            throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} must return IEnumerable<PromptStep> and accept one CommandContext.");
    }

    // A command that cannot describe itself forces HELP back onto a hand-written list, which
    // is exactly the second source of truth this table exists to remove.
    private static void ValidateMetadata(MethodInfo method, CommandMethodAttribute attribute)
    {
        if (attribute.Summary.Trim().Length == 0)
            throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} ({attribute.GlobalName}) must declare a Summary.");
    }

    // ----- Text -----

    /// <summary>Splits a command line into tokens, keeping quoted paths in one piece.</summary>
    internal static Queue<string> Tokenize(string line)
    {
        var tokens = new Queue<string>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) break;

            if (line[i] == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0)
                {
                    tokens.Enqueue(line[(i + 1)..]);
                    break;
                }

                tokens.Enqueue(line[(i + 1)..end]);
                i = end + 1;
                continue;
            }

            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            tokens.Enqueue(line[start..i]);
        }

        return tokens;
    }

    private static string Join(string first, string second) =>
        first.Length == 0 ? second
        : second.Length == 0 ? first
        : first + Environment.NewLine + second;

    private static bool IsCancel(string s) =>
        s.Equals("CANCEL", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("ESC",    StringComparison.OrdinalIgnoreCase) ||
        s.Equals("ESCAPE", StringComparison.OrdinalIgnoreCase);

    private sealed record Descriptor(CommandDescriptor Info, object Target, MethodInfo Method)
    {
        public string Name => Info.GlobalName;
        public CommandFlags Flags => Info.Flags;
    }
}
