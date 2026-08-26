using System;

namespace CloudScope.Rendering;

/// <summary>
/// Reused working set for <see cref="PointTileTraversal"/>, so a frame allocates nothing.
/// </summary>
/// <remarks>
/// Sized from the cell table, but the traversal only ever holds the frontier — the cells seen
/// but not yet visited — so this stays a few thousand entries even for a cloud with millions
/// of cells. It grows on demand rather than being sized for the worst case.
/// </remarks>
internal sealed class PointTileTraversalScratch
{
    private PointTileHeapEntry[] _entries = new PointTileHeapEntry[1024];
    private int _count;

    public PointTileHeap Reset()
    {
        _count = 0;
        return new PointTileHeap(this);
    }

    internal void Push(int index, float depth, float screenArea)
    {
        if (_count == _entries.Length)
            Array.Resize(ref _entries, _entries.Length * 2);

        _entries[_count] = new PointTileHeapEntry(index, depth, screenArea);
        // Sift up: the nearest cell rises to the root.
        int child = _count++;
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (_entries[parent].Depth <= _entries[child].Depth)
                break;
            (_entries[parent], _entries[child]) = (_entries[child], _entries[parent]);
            child = parent;
        }
    }

    internal bool TryPop(out int index, out float depth, out float screenArea)
    {
        if (_count == 0)
        {
            index = 0;
            depth = 0f;
            screenArea = 0f;
            return false;
        }

        PointTileHeapEntry root = _entries[0];
        index = root.Index;
        depth = root.Depth;
        screenArea = root.ScreenArea;

        _entries[0] = _entries[--_count];
        int parent = 0;
        while (true)
        {
            int left = parent * 2 + 1;
            if (left >= _count)
                break;

            int smallest = left + 1 < _count && _entries[left + 1].Depth < _entries[left].Depth
                ? left + 1
                : left;
            if (_entries[parent].Depth <= _entries[smallest].Depth)
                break;

            (_entries[parent], _entries[smallest]) = (_entries[smallest], _entries[parent]);
            parent = smallest;
        }

        return true;
    }

    private readonly record struct PointTileHeapEntry(int Index, float Depth, float ScreenArea);
}

/// <summary>Nearest-first view of a <see cref="PointTileTraversalScratch"/> for one frame.</summary>
internal readonly ref struct PointTileHeap
{
    private readonly PointTileTraversalScratch _scratch;

    internal PointTileHeap(PointTileTraversalScratch scratch) => _scratch = scratch;

    public void Push(int index, float depth, float screenArea) => _scratch.Push(index, depth, screenArea);

    public bool TryPop(out int index, out float depth, out float screenArea)
        => _scratch.TryPop(out index, out depth, out screenArea);
}
