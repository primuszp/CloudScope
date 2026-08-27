using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CloudScope.Store;

namespace CloudScope.Rendering;

/// <summary>Identifies one cell across every layer on screen.</summary>
/// <remarks>
/// Layer and node together, so one residency serves every open cloud and the least-recently
/// used cell is the least-recently used cell <em>anywhere</em>. A per-layer residency would
/// have to be told how to split the budget; this way a layer nobody is looking at simply stops
/// being touched and loses its pages to one that is being looked at.
/// </remarks>
internal readonly record struct PointTileCellKey(int LayerIndex, int NodeIndex);

/// <summary>A run of one layer's draw ranges, drawn together under that layer's tint.</summary>
internal readonly record struct PointTileLayerBatch(int LayerIndex, int FirstRange, int RangeCount);

/// <summary>
/// The GPU side of a streamed cloud: one large vertex buffer the residency writes pages of.
/// </summary>
/// <remarks>
/// A cell is stored on disk in exactly the layout the GPU reads, so a write is a copy rather
/// than a conversion — which is what lets the same interface sit over <c>BufferSubData</c> on
/// OpenGL and a shared <c>MTLBuffer</c> on Metal.
/// </remarks>
internal interface IPointTilePageBuffer
{
    /// <summary>
    /// Copies one page's worth of a cell into the buffer at <paramref name="firstPoint"/>.
    /// </summary>
    /// <param name="attributes">Empty when the store carries no attributes.</param>
    void WritePage(
        int firstPoint,
        ReadOnlySpan<GpuPointVertex> points,
        ReadOnlySpan<GpuPointAttribute> attributes);
}

/// <summary>
/// Keeps the cells the camera is looking at resident on the GPU, within a fixed page budget.
/// </summary>
/// <remarks>
/// Residency is what turns the on-disk store into something drawable without ever holding the
/// cloud in memory: the traversal names a few thousand cells per frame, this class draws the
/// ones already on the GPU, queues the rest for a background read, and evicts least-recently
/// used cells when the budget is full. Nothing here waits on disk — a frame draws whatever is
/// resident and the missing detail arrives over the next few frames, so camera motion stays
/// smooth while the picture sharpens behind it.
/// </remarks>
internal sealed class PointTileResidency : IDisposable
{
    /// <summary>
    /// Cell reads in flight at once. Enough to keep the disk busy, few enough that a camera
    /// turn does not leave a long queue of cells nobody is looking at any more.
    /// </summary>
    private const int MaxPendingLoads = 64;

    /// <summary>
    /// Pages uploaded per frame. The copy into the GPU buffer runs on the render thread, so
    /// letting a full queue land in one frame would stall it worse than the missing detail
    /// costs.
    /// </summary>
    private const int MaxPageUploadsPerFrame = 64;

    /// <summary>
    /// Frames a freed page waits before it may be handed out again.
    /// </summary>
    /// <remarks>
    /// An evicted cell can still be read by a command buffer the GPU has not finished, and
    /// writing its pages straight away would tear that frame. OpenGL's driver would rename the
    /// buffer behind the write to avoid it; a Metal shared buffer has no such protection, so
    /// the wait is done here and both backends get the same guarantee. Three frames is the
    /// depth both backends pipeline to.
    /// </remarks>
    private const int PageQuarantineFrames = 3;

    private readonly IReadOnlyList<PointTileLayer> _layers;
    private readonly IPointTilePageBuffer _buffer;
    private readonly PointTilePageTable _pages;

    private readonly Dictionary<PointTileCellKey, Entry> _resident = new();
    private readonly HashSet<PointTileCellKey> _inFlight = new();
    private readonly BlockingCollection<PointTileCellKey> _requests =
        new(new ConcurrentQueue<PointTileCellKey>(), MaxPendingLoads);
    private readonly ConcurrentQueue<Loaded> _completed = new();
    private readonly Queue<Quarantined> _quarantine = new();
    private readonly Thread _loader;
    private readonly CancellationTokenSource _shutdown = new();

    private int _frame;

    public PointTileResidency(
        IReadOnlyList<PointTileLayer> layers, IPointTilePageBuffer buffer, PointTilePageTable pages)
    {
        _layers = layers;
        _buffer = buffer;
        _pages = pages;
        _loader = new Thread(LoadLoop) { IsBackground = true, Name = "PointTileLoader" };
        _loader.Start();
    }

    public int ResidentCellCount => _resident.Count;

    /// <summary>Cells the traversal asked for that are not on the GPU yet.</summary>
    public int PendingCellCount => _inFlight.Count;

    public int FreePageCount => _pages.FreePageCount;

    /// <summary>
    /// Takes this frame's visible cells and emits the draw ranges for those already resident,
    /// queueing the rest. Cells not named here become eviction candidates.
    /// </summary>
    /// <param name="batches">
    /// Receives one batch per layer that has anything to draw. Ranges are grouped by layer
    /// rather than left in the traversal's depth order, so a layer's tint is set once and its
    /// pages go to the GPU in a single call instead of one per run of neighbouring cells.
    /// </param>
    /// <returns>How many batches were filled.</returns>
    public int BeginFrame(
        ReadOnlySpan<PointTileVisit> visible,
        Span<PointDrawRange> destination,
        Span<PointTileLayerBatch> batches,
        out long pointsDrawn)
    {
        _frame++;
        pointsDrawn = 0;

        ReleaseQuarantinedPages();
        PumpCompletedLoads();

        int rangeCount = 0;
        int batchCount = 0;
        for (int layerIndex = 0; layerIndex < _layers.Count && batchCount < batches.Length; layerIndex++)
        {
            if (!_layers[layerIndex].Visible)
                continue;

            int batchStart = rangeCount;
            for (int i = 0; i < visible.Length; i++)
            {
                if (visible[i].LayerIndex != layerIndex)
                    continue;

                var key = new PointTileCellKey(layerIndex, visible[i].NodeIndex);
                if (!_resident.TryGetValue(key, out Entry entry))
                {
                    Request(key);
                    continue;
                }

                // Touching the entry is what keeps it out of the eviction scan.
                _resident[key] = entry with { LastUsedFrame = _frame };

                if (rangeCount + entry.PageCount > destination.Length)
                    break;

                // One range per page: the pages of a cell need not be neighbours, and the draw
                // call takes them either way.
                int remaining = entry.PointCount;
                for (int page = 0; page < entry.PageCount; page++)
                {
                    int pointsOnPage = Math.Min(remaining, _pages.PageSizeInPoints);
                    destination[rangeCount++] = new PointDrawRange(
                        entry.Pages[page] * _pages.PageSizeInPoints, pointsOnPage);
                    remaining -= pointsOnPage;
                }

                pointsDrawn += entry.PointCount;
            }

            if (rangeCount > batchStart)
                batches[batchCount++] = new PointTileLayerBatch(layerIndex, batchStart, rangeCount - batchStart);
        }

        return batchCount;
    }

    /// <summary>Returns pages the GPU can no longer be reading to the free list.</summary>
    private void ReleaseQuarantinedPages()
    {
        while (_quarantine.TryPeek(out Quarantined oldest) && _frame - oldest.FreedFrame >= PageQuarantineFrames)
        {
            _pages.Free(_quarantine.Dequeue().Pages);
        }
    }

    /// <summary>Queues a cell for the loader; call order carries the nearest-first priority.</summary>
    private void Request(PointTileCellKey key)
    {
        if (_inFlight.Count >= MaxPendingLoads || !_inFlight.Add(key))
            return;

        // A full queue is not an error: the camera is moving faster than the disk, and the
        // cell will be asked for again next frame if it is still visible.
        if (!_requests.TryAdd(key))
            _inFlight.Remove(key);
    }

    /// <summary>Moves finished reads onto the GPU, making room first when the pages run out.</summary>
    private void PumpCompletedLoads()
    {
        int uploaded = 0;
        while (uploaded < MaxPageUploadsPerFrame && _completed.TryDequeue(out Loaded loaded))
        {
            _inFlight.Remove(loaded.Key);
            try
            {
                if (_resident.ContainsKey(loaded.Key))
                    continue;

                int pageCount = _pages.PagesFor(loaded.PointCount);
                var pages = new int[pageCount];
                if (!MakeRoom(pageCount) || !_pages.TryAllocate(pageCount, pages))
                    continue;

                int remaining = loaded.PointCount;
                for (int page = 0; page < pageCount; page++)
                {
                    int offset = page * _pages.PageSizeInPoints;
                    int pointsOnPage = Math.Min(remaining, _pages.PageSizeInPoints);
                    _buffer.WritePage(
                        pages[page] * _pages.PageSizeInPoints,
                        loaded.Points.AsSpan(offset, pointsOnPage),
                        loaded.Attributes is null
                            ? ReadOnlySpan<GpuPointAttribute>.Empty
                            : loaded.Attributes.AsSpan(offset, pointsOnPage));
                    remaining -= pointsOnPage;
                }

                _resident[loaded.Key] = new Entry(pages, pageCount, loaded.PointCount, _frame);
                uploaded += pageCount;
            }
            finally
            {
                ArrayPool<GpuPointVertex>.Shared.Return(loaded.Points);
                if (loaded.Attributes is not null)
                    ArrayPool<GpuPointAttribute>.Shared.Return(loaded.Attributes);
            }
        }
    }

    /// <summary>
    /// Evicts least-recently-used cells until <paramref name="pageCount"/> pages are free, and
    /// reports whether they are.
    /// </summary>
    /// <remarks>
    /// Cells drawn in the current frame are never evicted: dropping one to admit another the
    /// same frame would make the two fight over the budget every frame and neither would
    /// settle.
    /// </remarks>
    private bool MakeRoom(int pageCount)
    {
        if (pageCount > _pages.PageCount)
            return false;

        while (_pages.FreePageCount < pageCount)
        {
            // Pages already waiting out their quarantine will cover the request in a frame or
            // two. Evicting more on top of them would throw away cells for nothing.
            if (_pages.FreePageCount + QuarantinedPageCount >= pageCount)
                return false;

            PointTileCellKey oldest = default;
            bool found = false;
            int oldestFrame = int.MaxValue;
            foreach ((PointTileCellKey key, Entry entry) in _resident)
            {
                if (entry.LastUsedFrame < oldestFrame && entry.LastUsedFrame != _frame)
                {
                    oldest = key;
                    oldestFrame = entry.LastUsedFrame;
                    found = true;
                }
            }

            if (!found)
                return false;

            Evict(oldest);
        }

        return true;
    }

    private void Evict(PointTileCellKey key)
    {
        if (_resident.Remove(key, out Entry entry))
            _quarantine.Enqueue(new Quarantined(entry.Pages.AsSpan(0, entry.PageCount).ToArray(), _frame));
    }

    private int QuarantinedPageCount
    {
        get
        {
            int count = 0;
            foreach (Quarantined entry in _quarantine)
                count += entry.Pages.Length;
            return count;
        }
    }

    /// <summary>
    /// Reads requested cells out of the mapped store on a background thread.
    /// </summary>
    /// <remarks>
    /// Only the read happens here. The write into the GPU buffer runs on the render thread in
    /// <see cref="PumpCompletedLoads"/>, because neither backend's device context belongs to
    /// this thread.
    /// </remarks>
    private void LoadLoop()
    {
        try
        {
            foreach (PointTileCellKey key in _requests.GetConsumingEnumerable(_shutdown.Token))
            {
                if ((uint)key.LayerIndex >= (uint)_layers.Count)
                    continue;

                PointTileStore store = _layers[key.LayerIndex].Store;
                PointTileNode node = store.Nodes[key.NodeIndex];
                if (node.PointCount <= 0)
                    continue;

                GpuPointVertex[] points = ArrayPool<GpuPointVertex>.Shared.Rent(node.PointCount);
                GpuPointAttribute[]? attributes = null;
                store.ReadPoints(node, points);
                if (store.Header.HasAttributes)
                {
                    attributes = ArrayPool<GpuPointAttribute>.Shared.Rent(node.PointCount);
                    if (!store.ReadAttributes(node, attributes))
                    {
                        ArrayPool<GpuPointAttribute>.Shared.Return(attributes);
                        attributes = null;
                    }
                }

                _completed.Enqueue(new Loaded(key, node.PointCount, points, attributes));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _requests.CompleteAdding();
        _loader.Join(TimeSpan.FromSeconds(1));

        while (_completed.TryDequeue(out Loaded loaded))
        {
            ArrayPool<GpuPointVertex>.Shared.Return(loaded.Points);
            if (loaded.Attributes is not null)
                ArrayPool<GpuPointAttribute>.Shared.Return(loaded.Attributes);
        }

        foreach (Entry entry in _resident.Values)
            _pages.Free(entry.Pages.AsSpan(0, entry.PageCount));
        _resident.Clear();

        while (_quarantine.TryDequeue(out Quarantined quarantined))
            _pages.Free(quarantined.Pages);

        _requests.Dispose();
        _shutdown.Dispose();
    }

    /// <summary>Pages freed at <paramref name="FreedFrame"/>, not yet safe to reuse.</summary>
    private readonly record struct Quarantined(int[] Pages, int FreedFrame);

    private readonly record struct Entry(int[] Pages, int PageCount, int PointCount, int LastUsedFrame);

    private readonly record struct Loaded(
        PointTileCellKey Key, int PointCount, GpuPointVertex[] Points, GpuPointAttribute[]? Attributes);
}
