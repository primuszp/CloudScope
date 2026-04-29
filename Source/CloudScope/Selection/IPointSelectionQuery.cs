using System.Collections.Generic;

namespace CloudScope.Selection
{
    public interface IPointSelectionQuery
    {
        HashSet<int> Resolve(PointData[] points);
    }
}
