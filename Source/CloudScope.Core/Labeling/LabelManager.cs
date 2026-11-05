using System;
using CloudScope.Commands;
using System.Collections.Generic;

namespace CloudScope.Labeling
{
    /// <summary>
    /// Stores point-index → annotation mappings with undo support.
    /// Fires <see cref="LabelsChanged"/> whenever the label set is mutated
    /// so that the highlight renderer can rebuild its GPU buffer.
    /// </summary>
    public sealed class LabelManager(bool trackLocalHistory = true)
    {
        // ── Primary storage ──────────────────────────────────────────────────
        private readonly Dictionary<int, PointAnnotation> _annotations = new();

        // ── Undo stack ───────────────────────────────────────────────────────
        private readonly List<LabelAction> _undoStack = new();
        private readonly List<LabelAction> _redoStack = new();
        private const int MaxUndoDepth = 200;
        private long _revision, _savedRevision, _nextRevision;
        public long Revision => _revision;
        public bool HasUnsavedChanges => _revision != _savedRevision;
        public void MarkSaved() => _savedRevision = _revision;

        /// <summary>Raised after any mutation (apply / undo / clear / load).</summary>
        public event Action? LabelsChanged;

        /// <summary>
        /// Raised when a new reversible label change is made. The viewer records one undo
        /// action per raise, which is how a labelling command becomes a single undo step.
        /// </summary>
        public event Action<IUndoableAction>? ActionRecorded;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Read-only view of all current annotations.</summary>
        public IReadOnlyDictionary<int, PointAnnotation> AllAnnotations => _annotations;

        /// <summary>Number of annotated points.</summary>
        public int Count => _annotations.Count;

        /// <summary>Returns the label for a point, or null if unlabeled.</summary>
        public string? GetLabel(int pointIndex)
            => _annotations.TryGetValue(pointIndex, out var annotation) ? annotation.LabelName : null;

        /// <summary>Returns the full annotation for a point, or null if unlabeled.</summary>
        public PointAnnotation? GetAnnotation(int pointIndex)
            => _annotations.TryGetValue(pointIndex, out var annotation) ? annotation : null;

        /// <summary>
        /// Apply <paramref name="labelName"/> to every index in <paramref name="pointIndices"/>.
        /// Previous labels (or null for unlabeled) are recorded for undo.
        /// </summary>
        public void ApplyLabel(IReadOnlyCollection<int> pointIndices, string labelName)
            => ApplyAnnotation(pointIndices, new PointAnnotation(labelName, null));

        /// <summary>
        /// Apply <paramref name="annotation"/> to every index in <paramref name="pointIndices"/>.
        /// Previous annotations (or null for unlabeled) are recorded for undo.
        /// </summary>
        public void ApplyAnnotation(IReadOnlyCollection<int> pointIndices, PointAnnotation annotation)
        {
            if (pointIndices.Count == 0) return;

            var previous = new Dictionary<int, PointAnnotation?>(pointIndices.Count);
            foreach (int idx in pointIndices)
            {
                if (_annotations.TryGetValue(idx, out var old) && old == annotation) continue;
                previous.TryAdd(idx, _annotations.TryGetValue(idx, out old) ? old : null);
                _annotations[idx] = annotation;
            }

            if (previous.Count == 0) return;
            PushUndo(new LabelAction(previous, annotation));
            LabelsChanged?.Invoke();
        }

        /// <summary>Remove labels from a set of points (for eraser tool or clear).</summary>
        public void RemoveLabels(IReadOnlyCollection<int> pointIndices)
        {
            if (pointIndices.Count == 0) return;

            var previous = new Dictionary<int, PointAnnotation?>(pointIndices.Count);
            foreach (int idx in pointIndices)
            {
                if (!_annotations.Remove(idx, out var old)) continue;
                previous[idx] = old;
            }

            if (previous.Count == 0) return;
            PushUndo(new LabelAction(previous, null));
            LabelsChanged?.Invoke();
        }

        /// <summary>Undo the last label operation.</summary>
        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;

            int last = _undoStack.Count - 1;
            var action = _undoStack[last];
            _undoStack.RemoveAt(last);

            // Remember what the points looked like before the undo, so Redo can put it back.
            var current = new Dictionary<int, PointAnnotation?>(action.PreviousAnnotations.Count);
            foreach (var (idx, _) in action.PreviousAnnotations)
                current[idx] = _annotations.TryGetValue(idx, out var now) ? now : null;

            foreach (var (idx, previous) in action.PreviousAnnotations)
            {
                if (previous is null)
                    _annotations.Remove(idx);
                else
                    _annotations[idx] = previous.Value;
            }

            _revision = action.BeforeRevision;
            _redoStack.Add(action with { PreviousAnnotations = current });
            LabelsChanged?.Invoke();
            return true;
        }

        /// <summary>Reapplies the last undone label operation.</summary>
        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;

            int last = _redoStack.Count - 1;
            var action = _redoStack[last];
            _redoStack.RemoveAt(last);

            var current = new Dictionary<int, PointAnnotation?>(action.PreviousAnnotations.Count);
            foreach (var (idx, _) in action.PreviousAnnotations)
                current[idx] = _annotations.TryGetValue(idx, out var now) ? now : null;

            foreach (var (idx, restored) in action.PreviousAnnotations)
            {
                if (restored is null)
                    _annotations.Remove(idx);
                else
                    _annotations[idx] = restored.Value;
            }

            _revision = action.AfterRevision;
            _undoStack.Add(action with { PreviousAnnotations = current });
            LabelsChanged?.Invoke();
            return true;
        }

        /// <summary>Clear all labels (pushes one big undo frame).</summary>
        public void ClearAll()
        {
            if (_annotations.Count == 0) return;

            var previous = new Dictionary<int, PointAnnotation?>(_annotations.Count);
            foreach (var (idx, annotation) in _annotations)
                previous[idx] = annotation;

            _annotations.Clear();
            if (previous.Count == 0) return;
            PushUndo(new LabelAction(previous, null));
            LabelsChanged?.Invoke();
        }

        /// <summary>
        /// Replace all labels from an external source (e.g. file load).
        /// Clears undo history.
        /// </summary>
        public void LoadFrom(Dictionary<int, string> labels)
        {
            _annotations.Clear();
            foreach (var (idx, lbl) in labels)
                _annotations[idx] = new PointAnnotation(lbl, null);

            _undoStack.Clear();
            _redoStack.Clear();
            _revision = ++_nextRevision;
            MarkSaved();
            LabelsChanged?.Invoke();
        }

        /// <summary>
        /// Replace all annotations from an external source (e.g. file load).
        /// Clears undo history.
        /// </summary>
        public void LoadFromAnnotations(Dictionary<int, PointAnnotation> annotations)
        {
            _annotations.Clear();
            foreach (var (idx, annotation) in annotations)
                _annotations[idx] = annotation;

            _undoStack.Clear();
            _redoStack.Clear();
            _revision = ++_nextRevision;
            MarkSaved();
            LabelsChanged?.Invoke();
        }

        /// <summary>Clears document state without creating an undo operation.</summary>
        public void Reset()
        {
            _annotations.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            _revision = ++_nextRevision;
            MarkSaved();
            LabelsChanged?.Invoke();
        }

        /// <summary>Imports a complete annotation set as one reversible operation.</summary>
        public void ReplaceAnnotations(IReadOnlyDictionary<int, PointAnnotation> annotations)
        {
            if (_annotations.Count == annotations.Count && annotations.All(pair =>
                _annotations.TryGetValue(pair.Key, out var current) && current == pair.Value)) return;
            var previous = new Dictionary<int, PointAnnotation?>();
            foreach (int index in _annotations.Keys.Concat(annotations.Keys).Distinct())
                previous[index] = _annotations.TryGetValue(index, out var value) ? value : null;
            _annotations.Clear();
            foreach (var (index, annotation) in annotations) _annotations[index] = annotation;
            if (previous.Count == 0) return;
            PushUndo(new LabelAction(previous, null));
            LabelsChanged?.Invoke();
        }

        private void Restore(IReadOnlyDictionary<int, PointAnnotation?> snapshot, long revision)
        {
            foreach (var (index, annotation) in snapshot)
                if (annotation is { } value) _annotations[index] = value;
                else _annotations.Remove(index);
            _revision = revision;
            LabelsChanged?.Invoke();
        }

        // ── Internals ────────────────────────────────────────────────────────

        private void PushUndo(LabelAction action)
        {
            action = action with { BeforeRevision = _revision, AfterRevision = ++_nextRevision };
            _revision = action.AfterRevision;
            if (trackLocalHistory)
            {
                _undoStack.Add(action);
                _redoStack.Clear();
                while (_undoStack.Count > MaxUndoDepth) _undoStack.RemoveAt(0);
            }
            // Command history owns immutable changes, not calls into this manager's bounded
            // standalone history. Its undo depth can therefore exceed MaxUndoDepth safely.
            if (ActionRecorded is { } record)
            {
                var before = action.PreviousAnnotations;
                var after = before.Keys.ToDictionary(index => index,
                    index => _annotations.TryGetValue(index, out var value) ? (PointAnnotation?)value : null);
                record(new DelegateUndoAction("Label change", () => Restore(before, action.BeforeRevision),
                    () => Restore(after, action.AfterRevision), 256 + (long)before.Count * 128));
            }
        }

        /// <summary>One undo frame: snapshot of previous annotations + the annotation that was applied.</summary>
        internal sealed record LabelAction(
            Dictionary<int, PointAnnotation?> PreviousAnnotations,
            PointAnnotation? AppliedAnnotation, long BeforeRevision = 0, long AfterRevision = 0);
    }
}
