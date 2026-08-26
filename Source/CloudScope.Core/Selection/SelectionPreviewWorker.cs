using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloudScope.Selection
{
    public sealed class SelectionPreviewWorker : IDisposable
    {
        private readonly object _gate = new();
        private CancellationTokenSource? _cts;
        private Func<CancellationToken, PreviewResult>? _pendingWork;
        private int _version;
        private PreviewResult? _latest;
        private bool _dirty;
        private bool _running;
        private bool _disposed;

        /// <summary>
        /// The points a preview highlights, and which of them the volume caught.
        /// </summary>
        /// <remarks>
        /// The array travels with the indices because an out-of-core preview has no cloud in
        /// memory to index into — it reads the points it found and hands them over together.
        /// For an in-memory cloud the array is simply the cloud itself.
        /// </remarks>
        public readonly record struct PreviewResult(PointData[] Points, IReadOnlyList<int> Indices);

        public void Request(IPointSelectionQuery query, PointData[] points)
            => Request(token => new PreviewResult(points, query.Resolve(points, token)));

        /// <summary>
        /// Runs <paramref name="resolve"/> off the interaction thread, cancelling whatever it
        /// replaces. Only the newest request survives, so dragging a gizmo does not queue up a
        /// backlog of selections nobody will look at.
        /// </summary>
        public void Request(Func<CancellationToken, PreviewResult> resolve)
        {
            CancellationTokenSource cts;
            int version;

            lock (_gate)
            {
                if (_disposed) return;

                if (_running)
                {
                    _pendingWork = resolve;
                    _cts?.Cancel();
                    return;
                }

                (version, cts) = BeginRunLocked();
            }

            RunAsync(resolve, version, cts);
        }

        private (int version, CancellationTokenSource cts) BeginRunLocked()
        {
            _running = true;
            _cts = new CancellationTokenSource();
            return (++_version, _cts);
        }

        private void RunAsync(
            Func<CancellationToken, PreviewResult> resolve, int version, CancellationTokenSource cts)
        {
            CancellationToken token = cts.Token;
            Task.Run(() =>
            {
                try
                {
                    PreviewResult result = resolve(token);
                    Func<CancellationToken, PreviewResult>? next = null;
                    CancellationTokenSource? nextCts = null;
                    int nextVersion = 0;

                    lock (_gate)
                    {
                        if (version != _version)
                        {
                            cts.Dispose();
                            return;
                        }

                        if (!_disposed && !token.IsCancellationRequested)
                        {
                            _latest = result;
                            _dirty = true;
                        }

                        if (ReferenceEquals(_cts, cts))
                        {
                            _cts.Dispose();
                            _cts = null;
                        }

                        _running = false;

                        if (!_disposed && _pendingWork != null)
                        {
                            next = _pendingWork;
                            _pendingWork = null;
                            (nextVersion, nextCts) = BeginRunLocked();
                        }
                    }

                    if (next != null && nextCts != null)
                        RunAsync(next, nextVersion, nextCts);
                }
                catch (OperationCanceledException)
                {
                    bool shouldDispose = true;
                    Func<CancellationToken, PreviewResult>? next = null;
                    CancellationTokenSource? nextCts = null;
                    int nextVersion = 0;

                    lock (_gate)
                    {
                        if (ReferenceEquals(_cts, cts))
                        {
                            _cts.Dispose();
                            _cts = null;
                            shouldDispose = false;
                        }

                        if (version == _version)
                        {
                            _running = false;

                            if (!_disposed && _pendingWork != null)
                            {
                                next = _pendingWork;
                                _pendingWork = null;
                                (nextVersion, nextCts) = BeginRunLocked();
                            }
                        }
                    }

                    if (shouldDispose)
                        cts.Dispose();

                    if (next != null && nextCts != null)
                        RunAsync(next, nextVersion, nextCts);
                }
            }, CancellationToken.None);
        }

        public bool TryTakeLatest(out PreviewResult? result)
        {
            lock (_gate)
            {
                result = _latest;
                if (!_dirty)
                    return false;

                _dirty = false;
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _cts?.Cancel();
                _cts = null;
                _pendingWork = null;
                _running = false;
                _version++;
                if (_latest != null)
                {
                    _latest = null;
                    _dirty = true;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                _pendingWork = null;
                _disposed = true;
                _running = false;
            }
        }
    }
}
