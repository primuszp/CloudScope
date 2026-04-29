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
        private IPointSelectionQuery? _pendingQuery;
        private PointData[]? _pendingPoints;
        private int _version;
        private IReadOnlyList<int>? _latest;
        private bool _dirty;
        private bool _running;
        private bool _disposed;

        public void Request(IPointSelectionQuery query, PointData[] points)
        {
            CancellationTokenSource cts;
            int version;

            lock (_gate)
            {
                if (_disposed) return;

                if (_running)
                {
                    _pendingQuery = query;
                    _pendingPoints = points;
                    return;
                }

                (version, cts) = BeginRunLocked();
            }

            RunAsync(query, points, version, cts);
        }

        private (int version, CancellationTokenSource cts) BeginRunLocked()
        {
            _running = true;
            _cts = new CancellationTokenSource();
            return (++_version, _cts);
        }

        private void RunAsync(IPointSelectionQuery query, PointData[] points, int version, CancellationTokenSource cts)
        {
            CancellationToken token = cts.Token;
            Task.Run(() =>
            {
                try
                {
                    var result = query.Resolve(points, token);
                    IPointSelectionQuery? nextQuery = null;
                    PointData[]? nextPoints = null;
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

                        if (!_disposed && _pendingQuery != null && _pendingPoints != null)
                        {
                            nextQuery = _pendingQuery;
                            nextPoints = _pendingPoints;
                            _pendingQuery = null;
                            _pendingPoints = null;
                            (nextVersion, nextCts) = BeginRunLocked();
                        }
                    }

                    if (nextQuery != null && nextPoints != null && nextCts != null)
                        RunAsync(nextQuery, nextPoints, nextVersion, nextCts);
                }
                catch (OperationCanceledException)
                {
                    bool shouldDispose = true;
                    lock (_gate)
                    {
                        if (version == _version && ReferenceEquals(_cts, cts))
                        {
                            _cts.Dispose();
                            _cts = null;
                            _running = false;
                            shouldDispose = false;
                        }
                    }

                    if (shouldDispose)
                        cts.Dispose();
                }
            }, CancellationToken.None);
        }

        public bool TryTakeLatest(out IReadOnlyList<int>? indices)
        {
            lock (_gate)
            {
                indices = _latest;
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
                _pendingQuery = null;
                _pendingPoints = null;
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
                _pendingQuery = null;
                _pendingPoints = null;
                _disposed = true;
                _running = false;
            }
        }
    }
}
