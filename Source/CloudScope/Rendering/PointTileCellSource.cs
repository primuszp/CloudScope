using System.Buffers;

namespace CloudScope.Rendering;

internal interface IPointTileCellSource
{
    int LayerCount { get; }
    bool IsVisible(int layer);
    PointTileCellData Read(PointTileCellKey key, CancellationToken cancellation);
}

/// <summary>One owner for rented cell arrays, from the disk read through upload or cancellation.</summary>
internal sealed class PointTileCellData(int count, GpuPointVertex[] points, GpuPointAttribute[]? attributes = null) : IDisposable
{
    private bool _disposed;
    internal bool IsDisposed => _disposed;
    public int Count { get; } = count;
    public GpuPointVertex[] Points { get; } = points;
    public GpuPointAttribute[]? Attributes { get; } = attributes;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ArrayPool<GpuPointVertex>.Shared.Return(Points);
        if (Attributes != null) ArrayPool<GpuPointAttribute>.Shared.Return(Attributes);
    }
}

internal sealed class PointTileCellSource(IReadOnlyList<PointTileLayer> layers) : IPointTileCellSource
{
    public int LayerCount => layers.Count;
    public bool IsVisible(int layer) => layers[layer].Visible;
    public PointTileCellData Read(PointTileCellKey key, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var store = layers[key.LayerIndex].Store;
        var node = store.Nodes[key.NodeIndex];
        var points = ArrayPool<GpuPointVertex>.Shared.Rent(node.PointCount);
        GpuPointAttribute[]? attributes = null;
        try
        {
            store.ReadPoints(node, points);
            cancellation.ThrowIfCancellationRequested();
            if (store.Header.HasAttributes)
            {
                attributes = ArrayPool<GpuPointAttribute>.Shared.Rent(node.PointCount);
                if (!store.ReadAttributes(node, attributes))
                {
                    ArrayPool<GpuPointAttribute>.Shared.Return(attributes);
                    attributes = null;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            return new(node.PointCount, points, attributes);
        }
        catch
        {
            ArrayPool<GpuPointVertex>.Shared.Return(points);
            if (attributes != null) ArrayPool<GpuPointAttribute>.Shared.Return(attributes);
            throw;
        }
    }
}
