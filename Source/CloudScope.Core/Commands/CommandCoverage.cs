using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace CloudScope.Commands;

/// <summary>
/// Reports which of a target type's public API the command set actually reaches.
/// </summary>
/// <remarks>
/// This exists so the command inventory stops being a document someone has to remember to
/// update. It reads the IL of every registered command — following the compiler's iterator
/// state machine, since command bodies are coroutines — and lists the public methods on the
/// target type that no command calls. A capability that is not reachable by typing is a
/// finding, not a fact to be rediscovered later.
/// </remarks>
public static class CommandCoverage
{
    public sealed record Report(
        IReadOnlyList<string> Reached,
        IReadOnlyList<string> Unreached,
        IReadOnlyList<string> Ignored)
    {
        public int Total => Reached.Count + Unreached.Count;

        public string Describe()
        {
            var lines = new List<string>
            {
                $"Command coverage of the viewer API: {Reached.Count} of {Total} members reachable by command."
            };

            if (Unreached.Count > 0)
            {
                lines.Add("");
                lines.Add("Not reachable from any command:");
                lines.AddRange(Unreached.Select(name => "  " + name));
            }

            if (Ignored.Count > 0)
            {
                lines.Add("");
                lines.Add($"Not counted ({Ignored.Count}): input events, frame and lifecycle plumbing.");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Members that are deliberately not commands: continuous pointer and keyboard gestures,
    /// and the frame/lifecycle plumbing a host calls. A gesture is input, not a command — the
    /// same distinction that keeps orbit-drag out of the command set while ORBIT is in it.
    /// </summary>
    private static readonly string[] NotCommandSurface =
    [
        "MouseDown", "MouseUp", "MouseMove", "MouseWheel", "KeyDown",
        "UpdateFrame", "RenderFrame", "Resize", "Load", "Dispose",
        "SetViewportInset", "SetMode", "SetTool", "SetLabel",

        // The shell loads a cloud on a background thread and hands the result over; OPEN is
        // the command, and this is the API it and the shell both end up in.
        "LoadPointCloud", "SetLasFilePath"
    ];

    public static Report Analyse(IReadOnlyCollection<CommandDescriptor> commands, Type targetType)
    {
        HashSet<MethodBase> called = ReachableCalls(commands);

        var reached = new List<string>();
        var unreached = new List<string>();
        var ignored = new List<string>();

        foreach (MethodInfo method in PublicSurface(targetType))
        {
            string name = Describe(method);
            if (NotCommandSurface.Contains(method.Name))
            {
                ignored.Add(name);
                continue;
            }

            if (called.Contains(method))
                reached.Add(name);
            else
                unreached.Add(name);
        }

        reached.Sort(StringComparer.OrdinalIgnoreCase);
        unreached.Sort(StringComparer.OrdinalIgnoreCase);
        return new Report(reached, unreached, ignored);
    }

    /// <summary>
    /// Everything the command set calls, following the command classes' own private helpers.
    /// A command that delegates to a helper still reaches whatever the helper reaches, so
    /// stopping at the command body would report its work as unreachable.
    /// </summary>
    private static HashSet<MethodBase> ReachableCalls(IReadOnlyCollection<CommandDescriptor> commands)
    {
        HashSet<Type> commandTypes = commands
            .Select(command => command.Implementation?.DeclaringType)
            .Where(type => type != null)
            .Select(type => type!)
            .ToHashSet();

        var called = new HashSet<MethodBase>();
        var visited = new HashSet<MethodBase>();
        var queue = new Queue<MethodBase>(commands
            .Select(command => command.Implementation)
            .Where(method => method != null)
            .Select(method => (MethodBase)method!));

        while (queue.Count > 0)
        {
            MethodBase method = queue.Dequeue();
            if (!visited.Add(method))
                continue;

            foreach (MethodBase target in CalledMethods(method))
            {
                called.Add(target);
                if (IsOwnHelper(target, commandTypes))
                    queue.Enqueue(target);
            }
        }

        return called;
    }

    /// <summary>True for a method belonging to a command class itself, or to its iterators.</summary>
    private static bool IsOwnHelper(MethodBase method, HashSet<Type> commandTypes)
    {
        Type? declaring = method.DeclaringType;
        while (declaring != null)
        {
            if (commandTypes.Contains(declaring))
                return true;

            declaring = declaring.DeclaringType;
        }

        return false;
    }

    /// <summary>Public instance methods that change or report state, property accessors aside.</summary>
    private static IEnumerable<MethodInfo> PublicSurface(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.DeclaringType == type);

    private static string Describe(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    /// <summary>
    /// Every method called by a command body. A coroutine's code lives in the MoveNext of a
    /// compiler-generated class, so the state machine is followed rather than the stub.
    /// </summary>
    private static IEnumerable<MethodBase> CalledMethods(MethodBase command)
    {
        MethodBase body = ResolveBody(command);
        MethodBody? il = null;
        try
        {
            il = body.GetMethodBody();
        }
        catch (Exception)
        {
            // A method without accessible IL contributes nothing rather than breaking the report.
        }

        byte[]? code = il?.GetILAsByteArray();
        if (code == null)
            yield break;

        Type[] typeArguments = body.DeclaringType?.IsGenericType == true
            ? body.DeclaringType.GetGenericArguments()
            : [];

        foreach (int token in CallTokens(code))
        {
            MethodBase? resolved = null;
            try
            {
                resolved = body.Module.ResolveMethod(token, typeArguments, []);
            }
            catch (Exception)
            {
                // Tokens from other modules are not part of the surface being measured.
            }

            if (resolved != null)
                yield return resolved;
        }
    }

    private static MethodBase ResolveBody(MethodBase command)
    {
        var attribute = command.GetCustomAttribute<IteratorStateMachineAttribute>();
        MethodInfo? moveNext = attribute?.StateMachineType?
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return moveNext ?? command;
    }

    /// <summary>
    /// Walks the IL and yields the metadata token of every call, callvirt and newobj. The
    /// walk is opcode-aware: scanning for byte patterns would read operand bytes as opcodes
    /// and invent calls that are not there.
    /// </summary>
    private static IEnumerable<int> CallTokens(byte[] code)
    {
        int i = 0;
        while (i < code.Length)
        {
            short value = code[i];
            i++;
            if (value == 0xFE && i < code.Length)
            {
                value = (short)(0xFE00 | code[i]);
                i++;
            }

            if (!OperandSizes.TryGetValue(value, out (OperandType Type, int Size) operand))
                yield break; // Unknown opcode: stop rather than misread the rest.

            if (operand.Type is OperandType.InlineMethod && i + 4 <= code.Length &&
                value is CallOpcode or CallVirtOpcode or NewObjOpcode)
                yield return BitConverter.ToInt32(code, i);

            if (operand.Type == OperandType.InlineSwitch)
            {
                if (i + 4 > code.Length) yield break;
                int cases = BitConverter.ToInt32(code, i);
                i += 4 + cases * 4;
                continue;
            }

            i += operand.Size;
        }
    }

    private const short CallOpcode = 0x28;
    private const short CallVirtOpcode = 0x6F;
    private const short NewObjOpcode = 0x73;

    private static readonly Dictionary<short, (OperandType Type, int Size)> OperandSizes = BuildOpcodeTable();

    private static Dictionary<short, (OperandType, int)> BuildOpcodeTable()
    {
        var table = new Dictionary<short, (OperandType, int)>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode)
                continue;

            table[opcode.Value] = (opcode.OperandType, OperandSize(opcode.OperandType));
        }

        return table;
    }

    private static int OperandSize(OperandType type) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 0,
        _ => 4
    };
}
