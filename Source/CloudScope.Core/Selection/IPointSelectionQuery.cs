using System.Collections.Generic;
using System.Threading;

namespace CloudScope.Selection
{
    public interface IPointSelectionQuery
    {
        IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default);
    }
}
