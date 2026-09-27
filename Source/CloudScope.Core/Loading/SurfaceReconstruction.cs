using OpenTK.Mathematics;

namespace CloudScope.Loading;

public sealed record SurfaceMesh(Vector3[] Vertices, int[] Indices, double OriginX, double OriginY, double OriginZ);

/// <summary>Local PCA normals and greedy projection triangulation for a resident sample.</summary>
public static class SurfaceReconstruction
{
    public static SurfaceMesh Build(PointCloudDataset dataset, int neighbors = 15, float maximumEdge = 0,
        CancellationToken cancellation = default, IProgress<int>? progress = null)
    {
        if (dataset.VisibleCount < 3) throw new InvalidOperationException("At least three visible points are required.");
        if (dataset.VisibleCount > 200_000) throw new InvalidOperationException("Use THIN or FILTER to keep reconstruction at or below 200,000 points.");
        if (neighbors < 3 || neighbors > 32 || maximumEdge < 0 || !float.IsFinite(maximumEdge))
            throw new ArgumentOutOfRangeException(nameof(neighbors));
        var vertices = dataset.ViewPoints.Take(dataset.VisibleCount).Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
        var tree = new Tree(vertices);
        var normals = new Vector3[vertices.Length];
        var spacing = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            int[] near = tree.Nearest(i, neighbors);
            normals[i] = Normal(vertices, i, near);
            spacing[i] = near.Length == 0 ? 0 : (vertices[i] - vertices[near[0]]).Length;
            if (i % 1000 == 0) progress?.Report(i * 50 / vertices.Length);
        }
        if (maximumEdge == 0)
        {
            float[] positive = spacing.Where(s => s > 0).Order().ToArray();
            if (positive.Length == 0) throw new InvalidOperationException("All points are coincident.");
            maximumEdge = positive[positive.Length / 2] * 3;
        }
        var faces = new List<int>();
        var keys = new HashSet<(int, int, int)>();
        var edges = new Dictionary<(int, int), int>();
        float maxSquared = maximumEdge * maximumEdge;
        for (int i = 0; i < vertices.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            Vector3 n = normals[i];
            if (n.LengthSquared < 0.1f) continue;
            Vector3 u = Vector3.Cross(n, MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY).Normalized();
            Vector3 v = Vector3.Cross(n, u);
            var ring = tree.Nearest(i, neighbors).Where(j => (vertices[j] - vertices[i]).LengthSquared <= maxSquared)
                .Select(j => (Index: j, Angle: MathF.Atan2(Vector3.Dot(vertices[j] - vertices[i], v), Vector3.Dot(vertices[j] - vertices[i], u))))
                .OrderBy(p => p.Angle).ToArray();
            for (int j = 0; j < ring.Length; j++)
            {
                int b = ring[j].Index, c = ring[(j + 1) % ring.Length].Index;
                float angle = ring[(j + 1) % ring.Length].Angle - ring[j].Angle;
                if (angle <= 0) angle += 2 * MathF.PI;
                if (angle > MathF.PI / 2 + 0.001f || b == c || (vertices[b] - vertices[c]).LengthSquared > maxSquared) continue;
                Vector3 cross = Vector3.Cross(vertices[b] - vertices[i], vertices[c] - vertices[i]);
                if (cross.LengthSquared < 1e-14f) continue;
                int[] sorted = [i, b, c]; Array.Sort(sorted);
                var key = (sorted[0], sorted[1], sorted[2]);
                if (keys.Contains(key)) continue;
                var e1 = Edge(i, b); var e2 = Edge(b, c); var e3 = Edge(c, i);
                if (Count(e1) >= 2 || Count(e2) >= 2 || Count(e3) >= 2) continue;
                keys.Add(key); edges[e1] = Count(e1) + 1; edges[e2] = Count(e2) + 1; edges[e3] = Count(e3) + 1;
                faces.Add(i);
                if (Vector3.Dot(cross, n) >= 0) { faces.Add(b); faces.Add(c); }
                else { faces.Add(c); faces.Add(b); }
            }
            if (i % 1000 == 0) progress?.Report(50 + i * 50 / vertices.Length);
        }
        if (faces.Count == 0) throw new InvalidOperationException("No triangles found; adjust the maximum edge length.");
        return new SurfaceMesh(vertices, faces.ToArray(), dataset.OriginX, dataset.OriginY, dataset.OriginZ);
        int Count((int, int) edge) => edges.GetValueOrDefault(edge);
        static (int, int) Edge(int a, int b) => a < b ? (a, b) : (b, a);
    }

    private static Vector3 Normal(Vector3[] points, int index, int[] neighbors)
    {
        if (neighbors.Length < 2) return Vector3.Zero;
        Vector3 mean = points[index];
        foreach (int i in neighbors) mean += points[i];
        mean /= neighbors.Length + 1;
        var matrix = new double[3, 3];
        var eigen = new double[3, 3];
        for (int a = 0; a < 3; a++) eigen[a, a] = 1;
        foreach (int i in neighbors.Append(index))
        {
            Vector3 delta = points[i] - mean;
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) matrix[a, b] += delta[a] * delta[b];
        }
        for (int iteration = 0; iteration < 20; iteration++)
        {
            int p = 0, q = 1;
            for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
                if (Math.Abs(matrix[a, b]) > Math.Abs(matrix[p, q])) { p = a; q = b; }
            if (Math.Abs(matrix[p, q]) < 1e-12) break;
            double angle = 0.5 * Math.Atan2(2 * matrix[p, q], matrix[q, q] - matrix[p, p]);
            double c = Math.Cos(angle), s = Math.Sin(angle);
            for (int a = 0; a < 3; a++)
            {
                double ap = matrix[a, p], aq = matrix[a, q]; matrix[a, p] = c * ap - s * aq; matrix[a, q] = s * ap + c * aq;
                ap = eigen[a, p]; aq = eigen[a, q]; eigen[a, p] = c * ap - s * aq; eigen[a, q] = s * ap + c * aq;
            }
            for (int a = 0; a < 3; a++)
            {
                double pa = matrix[p, a], qa = matrix[q, a]; matrix[p, a] = c * pa - s * qa; matrix[q, a] = s * pa + c * qa;
            }
        }
        int smallest = 0;
        for (int i = 1; i < 3; i++) if (matrix[i, i] < matrix[smallest, smallest]) smallest = i;
        Vector3 normal = new((float)eigen[0, smallest], (float)eigen[1, smallest], (float)eigen[2, smallest]);
        int major = MathF.Abs(normal.X) > MathF.Abs(normal.Y) ? 0 : 1;
        if (MathF.Abs(normal.Z) > MathF.Abs(normal[major])) major = 2;
        return normal[major] < 0 ? -normal : normal;
    }

    private sealed class Tree
    {
        private sealed record Node(int Index, int Axis, Node? Left, Node? Right);
        private readonly Vector3[] _points;
        private readonly Node? _root;
        public Tree(Vector3[] points) { _points = points; _root = Build(Enumerable.Range(0, points.Length).ToArray(), 0); }
        private Node? Build(int[] indices, int depth)
        {
            if (indices.Length == 0) return null;
            int axis = depth % 3;
            Array.Sort(indices, (a, b) => _points[a][axis].CompareTo(_points[b][axis]));
            int mid = indices.Length / 2;
            return new Node(indices[mid], axis, Build(indices[..mid], depth + 1), Build(indices[(mid + 1)..], depth + 1));
        }
        public int[] Nearest(int index, int k)
        {
            var queue = new PriorityQueue<int, float>();
            Visit(_root);
            return queue.UnorderedItems.OrderBy(p => -p.Priority).Select(p => p.Element).ToArray();
            void Visit(Node? node)
            {
                if (node == null) return;
                float distance = (_points[index] - _points[node.Index]).LengthSquared;
                if (node.Index != index && distance > 1e-14f)
                {
                    queue.Enqueue(node.Index, -distance);
                    if (queue.Count > k) queue.Dequeue();
                }
                float delta = _points[index][node.Axis] - _points[node.Index][node.Axis];
                Visit(delta < 0 ? node.Left : node.Right);
                queue.TryPeek(out _, out float worst);
                if (queue.Count < k || delta * delta <= -worst) Visit(delta < 0 ? node.Right : node.Left);
            }
        }
    }
}
