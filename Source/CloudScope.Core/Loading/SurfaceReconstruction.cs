using OpenTK.Mathematics;

namespace CloudScope.Loading;

public sealed record SurfaceMesh(Vector3[] Vertices, int[] Indices, double OriginX, double OriginY, double OriginZ)
{
    public Vector3[] Normals { get; init; } = [];
    public Vector3[] Colors { get; init; } = [];
}

/// <summary>
/// Uniform-grid neighbourhoods, analytic PCA and angular fan reconstruction, matching
/// Open Pointcloud Studio's algorithm. Independently expressed in C# with cancellation
/// throughout; coordinates retain CloudScope's Z-up frame.
/// </summary>
public static class SurfaceReconstruction
{
    public static SurfaceMesh Build(PointCloudDataset dataset, int neighbors = 15, float maximumEdge = 0,
        CancellationToken cancellation = default, IProgress<int>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (dataset.VisibleCount < 3) throw new InvalidOperationException("At least three visible points are required.");
        if (neighbors < 3) throw new ArgumentOutOfRangeException(nameof(neighbors));
        if (maximumEdge < 0 || !float.IsFinite(maximumEdge)) throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        var vertices = dataset.ViewPoints.Take(dataset.VisibleCount).Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
        var min = vertices[0]; var max = min;
        for (int i = 0; i < vertices.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var p = vertices[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                throw new InvalidDataException("Reconstruction requires finite point coordinates.");
            min = Vector3.ComponentMin(min, p); max = Vector3.ComponentMax(max, p);
        }
        double extent = Math.Max((double)max.X - min.X, Math.Max((double)max.Y - min.Y, (double)max.Z - min.Z));
        double cellSize = extent / Math.Cbrt((double)vertices.Length / neighbors);
        if (cellSize == 0) cellSize = 1;
        double edge = maximumEdge == 0 ? 2 * cellSize : maximumEdge;
        progress?.Report(10);
        var grid = new SpatialGrid(vertices, cellSize, cancellation);
        var normals = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            normals[i] = Normal(vertices, i, grid.Nearest(i, neighbors, cancellation));
            if (i % 1000 == 0) progress?.Report(30 + (int)(30L * i / vertices.Length));
        }
        progress?.Report(60);
        var faces = new List<int>();
        var keys = new HashSet<(int, int, int)>();
        double maxSquared = edge * edge;
        for (int i = 0; i < vertices.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            Vector3 n = normals[i];
            // Compute the tangent frame in double precision, as in JavaScript's Number arithmetic.
            Vector3d nd = new(n.X, n.Y, n.Z);
            Vector3d u = Vector3d.Cross(nd, Math.Abs(n.X) < 0.9 ? Vector3d.UnitX : Vector3d.UnitY);
            if (u.Length < 1e-12) continue;
            u.Normalize();
            Vector3d v = Vector3d.Cross(nd, u);
            var ring = grid.Nearest(i, neighbors, cancellation)
                .Select(j => (Index: j, Delta: Difference(vertices[j], vertices[i])))
                .Where(p => p.Delta.LengthSquared <= maxSquared)
                .Select(p => (p.Index, Angle: Math.Atan2(Vector3d.Dot(p.Delta, v), Vector3d.Dot(p.Delta, u))))
                .OrderBy(p => p.Angle).ToArray();
            if (ring.Length < 2) continue;
            for (int j = 0; j < ring.Length; j++)
            {
                int next = (j + 1) % ring.Length;
                int b = ring[j].Index, c = ring[next].Index;
                double gap = ring[next].Angle - ring[j].Angle;
                if (gap < 0) gap += 2 * Math.PI;
                if (gap > Math.PI / 2 || b == c || Difference(vertices[b], vertices[c]).LengthSquared > maxSquared) continue;
                Vector3d cross = Vector3d.Cross(Difference(vertices[b], vertices[i]), Difference(vertices[c], vertices[i]));
                // The reference can emit zero-area faces for duplicates/collinear points.
                // Keep its topology for valid triangles but never publish such faces.
                if (cross.LengthSquared == 0) continue;
                int a0 = Math.Min(i, Math.Min(b, c)), a2 = Math.Max(i, Math.Max(b, c));
                int a1 = (int)((long)i + b + c - a0 - a2);
                if (!keys.Add((a0, a1, a2))) continue;
                faces.Add(i);
                if (Vector3d.Dot(cross, nd) >= 0) { faces.Add(b); faces.Add(c); }
                else { faces.Add(c); faces.Add(b); }
            }
            if (i % 1000 == 0) progress?.Report(60 + (int)(30L * i / vertices.Length));
        }
        cancellation.ThrowIfCancellationRequested();
        if (faces.Count == 0) throw new InvalidOperationException("No triangles found; increase the neighbor count or maximum edge length.");
        progress?.Report(100);
        return new SurfaceMesh(vertices, faces.ToArray(), dataset.OriginX, dataset.OriginY, dataset.OriginZ) { Normals = normals, Colors = dataset.HasColor
            ? dataset.ViewPoints.Take(dataset.VisibleCount).Select(p => new Vector3(p.R, p.G, p.B)).ToArray() : [] };
    }

    private static Vector3d Difference(Vector3 a, Vector3 b) => new((double)a.X - b.X, (double)a.Y - b.Y, (double)a.Z - b.Z);

    private static Vector3 Normal(Vector3[] points, int index, int[] neighbors)
    {
        if (neighbors.Length < 3) return Vector3.UnitZ; // Reference's up fallback mapped to CloudScope.
        Vector3d mean = Vector3d.Zero;
        foreach (int i in neighbors) mean += new Vector3d(points[i].X, points[i].Y, points[i].Z);
        mean /= neighbors.Length;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (int i in neighbors)
        {
            Vector3d d = new Vector3d(points[i].X, points[i].Y, points[i].Z) - mean;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
            yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }
        Vector3d normal = SmallestEigenvector(xx, xy, xz, yy, yz, zz);
        Vector3d fromMean = new Vector3d(points[index].X, points[index].Y, points[index].Z) - mean;
        if (Vector3d.Dot(normal, fromMean) < 0) normal = -normal;
        return new Vector3((float)normal.X, (float)normal.Y, (float)normal.Z);
    }

    private static Vector3d SmallestEigenvector(double xx, double xy, double xz, double yy, double yz, double zz)
    {
        // Analytic eigenvalues of a real symmetric 3x3 covariance matrix.
        double center = (xx + yy + zz) / 3;
        double sx = xx - center, sy = yy - center, sz = zz - center;
        double scale = Math.Sqrt((sx * sx + sy * sy + sz * sz + 2 * (xy * xy + xz * xz + yz * yz)) / 6);
        if (scale < 1e-15) return Vector3d.UnitZ;
        double a = sx / scale, b = xy / scale, c = xz / scale, d = sy / scale, e = yz / scale, f = sz / scale;
        double determinant = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
        double angle = Math.Acos(Math.Clamp(determinant / 2, -1, 1)) / 3;
        double eigenvalue = center + 2 * scale * Math.Cos(angle + 2 * Math.PI / 3);
        Vector3d r0 = new(xx - eigenvalue, xy, xz), r1 = new(xy, yy - eigenvalue, yz), r2 = new(xz, yz, zz - eigenvalue);
        Vector3d vector = Vector3d.Cross(r0, r1);
        if (vector.Length < 1e-12) vector = Vector3d.Cross(r1, r2);
        if (vector.Length < 1e-12) vector = Vector3d.Cross(r0, r2);
        return vector.Length < 1e-12 ? Vector3d.UnitZ : vector.Normalized();
    }

    private sealed class SpatialGrid
    {
        private readonly Vector3[] _points;
        private readonly double _size;
        private readonly Dictionary<(long, long, long), List<int>> _cells = [];
        public SpatialGrid(Vector3[] points, double size, CancellationToken cancellation)
        {
            _points = points; _size = size;
            for (int i = 0; i < points.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var key = Cell(points[i]);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
                list.Add(i);
            }
        }
        private (long X, long Y, long Z) Cell(Vector3 p) =>
            (checked((long)Math.Floor(p.X / _size)), checked((long)Math.Floor(p.Y / _size)), checked((long)Math.Floor(p.Z / _size)));
        public int[] Nearest(int index, int k, CancellationToken cancellation)
        {
            var center = Cell(_points[index]);
            var candidates = new List<(int Index, double Distance, int Order)>();
            for (int radius = 1; radius <= 5; radius++)
            {
                candidates.Clear();
                for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                for (int z = -radius; z <= radius; z++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!_cells.TryGetValue((center.X + x, center.Y + y, center.Z + z), out var cell)) continue;
                    foreach (int other in cell)
                    {
                        if (other == index) continue;
                        candidates.Add((other, Difference(_points[other], _points[index]).LengthSquared, candidates.Count));
                    }
                }
                if (candidates.Count >= k) break;
            }
            // Tie order follows cell traversal and insertion, matching JS's stable sort.
            candidates.Sort((a, b) => { int comparison = a.Distance.CompareTo(b.Distance); return comparison != 0 ? comparison : a.Order.CompareTo(b.Order); });
            return candidates.Take(k).Select(p => p.Index).ToArray();
        }
    }
}
