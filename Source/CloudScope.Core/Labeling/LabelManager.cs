using System;
using System.Collections.Generic;

namespace CloudScope.Labeling
{
    /// <summary>
    /// Stores point-index → annotation mappings with undo support.
    /// Fires <see cref="LabelsChanged"/> whenever the label set is mutated
    /// so that the highlight renderer can rebuild its GPU buffer.
    /// </summary>
    public sealed class LabelManager
    {
        // ── Primary storage ──────────────────────────────────────────────────
        private readonly Dictionary<int, PointAnnotation> _annotations = new();

        // ── Undo stack ───────────────────────────────────────────────────────
        private readonly List<LabelAction> _undoStack = new();
        private const int MaxUndoDepth = 200;

        /// <summary>Raised after any mutation (apply / undo / clear / load).</summary>
        public event Action? LabelsChanged;

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
                previous[idx] = _annotations.TryGetValue(idx, out var old) ? old : null;
                _annotations[idx] = annotation;
            }

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
                previous[idx] = _annotations.TryGetValue(idx, out var old) ? old : null;
                _annotations.Remove(idx);
            }

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
            foreach (var (idx, previous) in action.PreviousAnnotations)
            {
                if (previous is null)
                    _annotations.Remove(idx);
                else
                    _annotations[idx] = previous.Value;
            }

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
            LabelsChanged?.Invoke();
        }

        // ── Internals ────────────────────────────────────────────────────────

        private void PushUndo(LabelAction action)
        {
            _undoStack.Add(action);
            while (_undoStack.Count > MaxUndoDepth)
                _undoStack.RemoveAt(0);
        }

        /// <summary>One undo frame: snapshot of previous annotations + the annotation that was applied.</summary>
        internal sealed record LabelAction(
            Dictionary<int, PointAnnotation?> PreviousAnnotations,
            PointAnnotation? AppliedAnnotation);
    }
}
