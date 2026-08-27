using System;

namespace CloudScope.Rendering;

/// <summary>
/// Divides the GPU's point budget into fixed-size pages and hands them out one cell at a time.
/// </summary>
/// <remarks>
/// Cells vary in size by more than an order of magnitude, so allocating a whole cell as one
/// contiguous run would fragment the buffer as the camera moves and cells are evicted. Pages
/// avoid that entirely: a cell takes any free pages, contiguous or not, because a scattered
/// cell costs extra <em>entries</em> in the frame's draw range list rather than extra draw
/// calls — the same <c>MultiDrawArrays</c> that already draws the resident path takes
/// arbitrary <c>(first, count)</c> pairs. Allocation and release are then a stack pop and
/// push, and the only waste is the tail of each cell's last page.
/// </remarks>
internal sealed class PointTilePageTable
{
    private readonly int[] _free;
    private int _freeCount;

    public PointTilePageTable(int pageSizeInPoints, int pageCount)
    {
        if (pageSizeInPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSizeInPoints));
        if (pageCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        PageSizeInPoints = pageSizeInPoints;
        PageCount = pageCount;

        // Handed out from the top, so the first cells of a fresh cloud land at the start of
        // the buffer and a partly filled buffer stays packed towards it.
        _free = new int[pageCount];
        for (int i = 0; i < pageCount; i++)
            _free[i] = pageCount - 1 - i;
        _freeCount = pageCount;
    }

    public int PageSizeInPoints { get; }

    public int PageCount { get; }

    public int FreePageCount => _freeCount;

    /// <summary>Points the whole table can hold.</summary>
    public long CapacityInPoints => (long)PageCount * PageSizeInPoints;

    /// <summary>Pages a cell of <paramref name="pointCount"/> points needs.</summary>
    public int PagesFor(int pointCount) => (pointCount + PageSizeInPoints - 1) / PageSizeInPoints;

    /// <summary>
    /// Takes <paramref name="count"/> pages, or returns false and takes none.
    /// </summary>
    public bool TryAllocate(int count, Span<int> pages)
    {
        if (count > _freeCount || pages.Length < count)
            return false;

        for (int i = 0; i < count; i++)
            pages[i] = _free[--_freeCount];

        return true;
    }

    public void Free(ReadOnlySpan<int> pages)
    {
        foreach (int page in pages)
            _free[_freeCount++] = page;
    }
}
