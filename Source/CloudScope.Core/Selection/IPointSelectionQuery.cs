using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// A selection volume, asked one point at a time.
    /// </summary>
    /// <remarks>
    /// Split into a containment test and a bounding box so the same volume can be resolved
    /// against an array in memory or against a cloud that only exists on disk. Out of core the
    /// box is what makes the query affordable: a cell whose bounds miss the volume is skipped
    /// without being read, so a selection touches a fraction of the file rather than all of it.
    /// </remarks>
    public interface IPointSelectionQuery
    {
        /// <summary>Whether the point lies inside the volume.</summary>
        bool Contains(float x, float y, float z);

        /// <summary>
        /// A box that contains the volume. May be larger than the volume, never smaller: a
        /// box that cut a corner would drop points the volume does hold.
        /// </summary>
        void GetBounds(out Vector3 min, out Vector3 max);

        /// <summary>Whether the volume is too small to select anything.</summary>
        bool IsEmpty { get; }

        /// <summary>Indices of the points of <paramref name="points"/> inside the volume.</summary>
        IReadOnlyList<int> Resolve(PointData[] points, CancellationToken cancellationToken = default);
    }

    /// <summary>Shared implementation of <see cref="IPointSelectionQuery.Resolve"/>.</summary>
    public static class PointSelectionQuery
    {
        public static IReadOnlyList<int> Resolve(
            IPointSelectionQuery query, PointData[] points, CancellationToken cancellationToken)
        {
            if (query.IsEmpty)
                return Array.Empty<int>();

            query.GetBounds(out Vector3 min, out Vector3 max);
            var list = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                if ((i & 4095) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                PointData point = points[i];
                // The box first: it rejects most points with three compares, and the volume's
                // own test is the expensive one for a rotated box or a cylinder.
                if (point.X < min.X || point.X > max.X
                    || point.Y < min.Y || point.Y > max.Y
                    || point.Z < min.Z || point.Z > max.Z)
                    continue;

                if (query.Contains(point.X, point.Y, point.Z))
                    list.Add(i);
            }

            return list;
        }
    }
}
