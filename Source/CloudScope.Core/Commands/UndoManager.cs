namespace CloudScope.Commands;

/// <summary>One reversible change made by a command.</summary>
public interface IUndoableAction
{
    string Description { get; }
    long EstimatedBytes => 256;
    void Undo();
    void Redo();
}

/// <summary>
/// Groups the changes a command makes into one reversible step, the way AutoCAD groups a
/// command's database changes into an undoable operation.
/// </summary>
/// <remarks>
/// The runtime opens a mark when a command starts and closes it when the command ends, so
/// UNDO steps back by whole commands rather than by whatever internal operations a command
/// happened to perform. A command declaring <see cref="CommandFlags.NoUndoMarker"/> opens no
/// mark: inquiry and view commands should not consume an undo step.
/// </remarks>
public sealed class UndoManager(long memoryBudgetBytes = 128L * 1024 * 1024, int maximumCommands = 1000)
{
    public event Action? HistoryTrimmed;
    public long EstimatedRetainedBytes => _undo.Concat(_redo).Sum(group => group.EstimatedBytes);
    public long MemoryBudgetBytes { get; } = Math.Max(0, memoryBudgetBytes);
    private readonly int _maximumCommands = Math.Max(0, maximumCommands);
    private readonly List<Group> _undo = [];
    private readonly List<Group> _redo = [];
    private Group? _open;

    /// <summary>Named positions in the undo history, set by UNDO Mark and returned to by UNDO Back.</summary>
    private readonly List<int> _marks = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int Depth => _undo.Count;

    /// <summary>Name of the command that would be undone next, or "".</summary>
    public string NextUndoName => _undo.Count > 0 ? _undo[^1].Name : "";

    public void RunSeparate(string name, Action change)
    {
        Group? suspended = _open;
        _open = null;
        BeginMark(name);
        try { change(); Commit(); }
        catch { Rollback(); throw; }
        finally { _open = suspended; }
    }

    public void BeginMark(string commandName)
    {
        // A command that starts while another mark is open (a transparent command) joins it
        // rather than splitting the group in two.
        _open ??= new Group(commandName);
    }

    public void Record(IUndoableAction action)
    {
        if (_open is { } group) group.Actions.Add(action);
        else
        {
            // Pointer-driven edits can finish outside a modal command. They still use the
            // same history, rather than a second label-only undo path.
            var standalone = new Group(action.Description);
            standalone.Actions.Add(action);
            _undo.Add(standalone);
            _redo.Clear();
            TrimToBudget();
        }
    }

    public void Commit()
    {
        if (_open == null) return;

        Group group = _open;
        _open = null;
        if (group.Actions.Count == 0) return;

        _undo.Add(group);
        _redo.Clear();
        TrimToBudget();
    }

    /// <summary>Reverses everything the open command did, for a cancelled or failed command.</summary>
    public void Rollback()
    {
        if (_open == null) return;

        Group group = _open;
        _open = null;
        for (int i = group.Actions.Count - 1; i >= 0; i--)
            group.Actions[i].Undo();
    }

    /// <summary>Undoes <paramref name="count"/> commands; returns how many were undone.</summary>
    public int Undo(int count = 1)
    {
        int done = 0;
        while (done < count && _undo.Count > 0)
        {
            Group group = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            for (int i = group.Actions.Count - 1; i >= 0; i--)
                group.Actions[i].Undo();

            _redo.Add(group);
            done++;
        }

        TrimMarks();
        return done;
    }

    /// <summary>Redoes <paramref name="count"/> previously undone commands.</summary>
    public int Redo(int count = 1)
    {
        int done = 0;
        while (done < count && _redo.Count > 0)
        {
            Group group = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            foreach (IUndoableAction action in group.Actions)
                action.Redo();

            _undo.Add(group);
            done++;
        }

        return done;
    }

    public void SetMark() => _marks.Add(_undo.Count);

    /// <summary>Undoes back to the last mark; returns how many commands were undone, or -1 when there is no mark.</summary>
    public int Back()
    {
        if (_marks.Count == 0)
            return -1;

        int target = _marks[^1];
        _marks.RemoveAt(_marks.Count - 1);
        return Undo(Math.Max(_undo.Count - target, 0));
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _marks.Clear();
        _open = null;
    }

    private void TrimToBudget()
    {
        bool trimmed = false;
        while (_undo.Count + _redo.Count > _maximumCommands || EstimatedRetainedBytes > MemoryBudgetBytes)
        {
            if (_undo.Count > 0)
            {
                _undo.RemoveAt(0);
                for (int i = 0; i < _marks.Count; i++) _marks[i] = Math.Max(0, _marks[i] - 1);
            }
            else if (_redo.Count > 0) _redo.RemoveAt(0);
            else break;
            trimmed = true;
        }
        if (trimmed) HistoryTrimmed?.Invoke();
    }

    private void TrimMarks() => _marks.RemoveAll(mark => mark > _undo.Count);

    private sealed class Group(string name)
    {
        public long EstimatedBytes => Actions.Sum(action => Math.Max(0, action.EstimatedBytes));
        public string Name { get; } = name;
        public List<IUndoableAction> Actions { get; } = [];
    }
}

/// <summary>An action defined by two delegates, for callers that already have both directions.</summary>
public sealed class DelegateUndoAction(string description, Action undo, Action redo, long estimatedBytes = 256) : IUndoableAction
{
    public string Description { get; } = description;
    public long EstimatedBytes { get; } = Math.Max(0, estimatedBytes);
    public void Undo() => undo();
    public void Redo() => redo();
}
