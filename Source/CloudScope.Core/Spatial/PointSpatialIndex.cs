using OpenTK.Mathematics;

namespace CloudScope.Spatial;

/// <summary>Balanced k-d index with exact Euclidean nearest-neighbour queries.</summary>
internal sealed class PointSpatialIndex
{
    private readonly Vector3[] _points;
    private readonly int[] _order;
    private readonly Comparison<int> _tieOrder;

    public PointSpatialIndex(Vector3[] points, Comparison<int>? tieOrder = null,
        CancellationToken cancellation = default)
    {
        _points = points;
        _order = Enumerable.Range(0, points.Length).ToArray();
        _tieOrder = tieOrder ?? ((a, b) => a.CompareTo(b));
        Build(0, _order.Length, 0);

        void Build(int start, int end, int axis)
        {
            cancellation.ThrowIfCancellationRequested();
            if (end - start < 2) return;
            int middle = start + (end - start) / 2;
            // Median selection avoids sorting every subtree (O(n log n) construction).
            int left = start, right = end - 1;
            while (left < right)
            {
                cancellation.ThrowIfCancellationRequested();
                int pivot = _order[left + (right - left) / 2];
                int i = left, j = right;
                while (i <= j)
                {
                    while (Compare(_order[i], pivot, axis) < 0) i++;
                    while (Compare(_order[j], pivot, axis) > 0) j--;
                    if (i <= j) { (_order[i], _order[j]) = (_order[j], _order[i]); i++; j--; }
                }
                if (middle <= j) right = j;
                else if (middle >= i) left = i;
                else break;
            }
            Build(start, middle, (axis + 1) % 3);
            Build(middle + 1, end, (axis + 1) % 3);
        }
    }

    public int[] Nearest(int index, int count, CancellationToken cancellation = default)
    {
        count = Math.Min(count, _points.Length - 1);
        if (count <= 0) return [];
        // Reverse priority: the worst retained neighbour is removed first.
        var comparer = Comparer<(double Distance, int Index)>.Create((a, b) =>
        {
            int result = b.Distance.CompareTo(a.Distance);
            return result != 0 ? result : _tieOrder(b.Index, a.Index);
        });
        var nearest = new PriorityQueue<int, (double Distance, int Index)>(comparer);
        Search(0, _order.Length, 0);
        return nearest.UnorderedItems.OrderBy(p => p.Priority.Distance)
            .ThenBy(p => p.Element, Comparer<int>.Create(_tieOrder)).Select(p => p.Element).ToArray();

        void Search(int start, int end, int axis)
        {
            cancellation.ThrowIfCancellationRequested();
            if (start >= end) return;
            int middle = start + (end - start) / 2;
            int other = _order[middle];
            Vector3 p = _points[other], query = _points[index];
            double dx = (double)p.X - query.X, dy = (double)p.Y - query.Y, dz = (double)p.Z - query.Z;
            if (other != index)
            {
                nearest.Enqueue(other, (dx * dx + dy * dy + dz * dz, other));
                if (nearest.Count > count) nearest.Dequeue();
            }
            double split = (double)query[axis] - p[axis];
            if (split <= 0) Search(start, middle, (axis + 1) % 3);
            else Search(middle + 1, end, (axis + 1) % 3);
            nearest.TryPeek(out _, out var worst);
            if (nearest.Count < count || split * split <= worst.Distance)
            {
                if (split <= 0) Search(middle + 1, end, (axis + 1) % 3);
                else Search(start, middle, (axis + 1) % 3);
            }
        }
    }

    private int Compare(int a, int b, int axis)
    {
        int result = _points[a][axis].CompareTo(_points[b][axis]);
        return result != 0 ? result : a.CompareTo(b);
    }
}
