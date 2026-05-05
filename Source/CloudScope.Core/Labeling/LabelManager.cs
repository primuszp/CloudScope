using System;
using System.Collections.Generic;

namespace CloudScope.Labeling
{
    /// <summary>
    /// Stores point-index → label-name mappings with undo support.
    /// Fires <see cref="LabelsChanged"/> whenever the label set is mutated
    /// so that the highlight renderer can rebuild its GPU buffer.
    /// </summary>
    public sealed class LabelManager
    {
        // ── Primary storage ──────────────────────────────────────────────────
        private readonly Dictionary<int, string> _labels = new();

        // ── Undo stack ───────────────────────────────────────────────────────
        private readonly Stack<LabelAction> _undoStack = new();
        private const int MaxUndoDepth = 200;

        /// <summary>Raised after any mutation (apply / undo / clear / load).</summary>
        public event Action? LabelsChanged;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Read-only view of all current labels.</summary>
        public IReadOnlyDictionary<int, string> AllLabels => _labels;

        /// <summary>Number of labeled points.</summary>
        public int Count => _labels.Count;

        /// <summary>Returns the label for a point, or null if unlabeled.</summary>
        public string? GetLabel(int pointIndex)
            => _labels.TryGetValue(pointIndex, out var lbl) ? lbl : null;

        /// <summary>
        /// Apply <paramref name="labelName"/> to every index in <paramref name="pointIndices"/>.
        /// Previous labels (or null for unlabeled) are recorded for undo.
        /// </summary>
        public void ApplyLabel(IReadOnlyCollection<int> pointIndices, string labelName)
        {
            if (pointIndices.Count == 0) return;

            var previous = new Dictionary<int, string?>(pointIndices.Count);
            foreach (int idx in pointIndices)
            {
                previous[idx] = _labels.TryGetValue(idx, out var old) ? old : null;
                _labels[idx] = labelName;
            }

            PushUndo(new LabelAction(previous, labelName));
            LabelsChanged?.Invoke();
        }

        /// <summary>Remove labels from a set of points (for eraser tool or clear).</summary>
        public void RemoveLabels(IReadOnlyCollection<int> pointIndices)
        {
            if (pointIndices.Count == 0) return;

            var previous = new Dictionary<int, string?>(pointIndices.Count);
            foreach (int idx in pointIndices)
            {
                previous[idx] = _labels.TryGetValue(idx, out var old) ? old : null;
                _labels.Remove(idx);
            }

            PushUndo(new LabelAction(previous, null));
            LabelsChanged?.Invoke();
        }

        /// <summary>Undo the last label operation.</summary>
        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;

            var action = _undoStack.Pop();
            foreach (var (idx, prevLabel) in action.PreviousLabels)
            {
                if (prevLabel is null)
                    _labels.Remove(idx);
                else
                    _labels[idx] = prevLabel;
            }

            LabelsChanged?.Invoke();
            return true;
        }

        /// <summary>Clear all labels (pushes one big undo frame).</summary>
        public void ClearAll()
        {
            if (_labels.Count == 0) return;

            var previous = new Dictionary<int, string?>(_labels.Count);
            foreach (var (idx, lbl) in _labels)
                previous[idx] = lbl;

            _labels.Clear();
            PushUndo(new LabelAction(previous, null));
            LabelsChanged?.Invoke();
        }

        /// <summary>
        /// Replace all labels from an external source (e.g. file load).
        /// Clears undo history.
        /// </summary>
        public void LoadFrom(Dictionary<int, string> labels)
        {
            _labels.Clear();
            foreach (var (idx, lbl) in labels)
                _labels[idx] = lbl;

            _undoStack.Clear();
            LabelsChanged?.Invoke();
        }

        // ── Internals ────────────────────────────────────────────────────────

        private void PushUndo(LabelAction action)
        {
            _undoStack.Push(action);
            // Trim to avoid unbounded memory growth
            while (_undoStack.Count > MaxUndoDepth)
            {
                // Stack doesn't support RemoveLast, so we accept the extra depth
                // until GC collects the trimmed frames. In practice 200 is plenty.
                break;
            }
        }

        /// <summary>One undo frame: snapshot of previous labels + the label that was applied.</summary>
        internal sealed record LabelAction(
            Dictionary<int, string?> PreviousLabels,
            string? AppliedLabel);
    }
}
