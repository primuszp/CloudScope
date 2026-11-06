using CloudScope.Loading;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;
using NetTopologySuite.Triangulate.QuadEdge;
using OpenTK.Mathematics;

namespace CloudScope.Terrain;

public sealed record TerrainOptions(double CellSize = 1, double MaximumEdge = 5, int MaximumSites = 200_000);

/// <summary>Single-valued XY terrain: lowest actual source point per grid cell, robust Delaunay topology and an explicit gap limit.</summary>
public static class TerrainTriangulation
{
    public static SurfaceMesh Build(IReadOnlyList<PointData> points, TerrainOptions options,
        (double X,double Y,double Z) origin, CancellationToken cancellation = default, IProgress<int>? progress = null,
        Func<int,bool>? include = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y) || !double.IsFinite(origin.Z))
            throw new ArgumentException("TIN requires a finite survey origin.");
        if (!double.IsFinite(options.CellSize) || options.CellSize < 0 || !double.IsFinite(options.MaximumEdge) || options.MaximumEdge <= 0
            || options.MaximumSites < 3 || options.MaximumSites > 1_000_000)
            throw new ArgumentException("TIN requires a nonnegative cell size, positive maximum edge and 3–1,000,000 sites.");
        var sites = new Dictionary<(double X,double Y),Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            if ((i & 4095) == 0) { cancellation.ThrowIfCancellationRequested(); progress?.Report((int)(20L*i/Math.Max(1,points.Count))); }
            if (include != null && !include(i)) continue;
            var p = points[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) throw new InvalidDataException("TIN requires finite coordinates.");
            var key = options.CellSize == 0 ? ((double)p.X,(double)p.Y)
                : (Math.Floor(p.X/options.CellSize),Math.Floor(p.Y/options.CellSize));
            if (!double.IsFinite(key.Item1) || !double.IsFinite(key.Item2))
                throw new ArgumentException("TIN cell size is too small for the source coordinate range.");
            var value = new Vector3(p.X,p.Y,p.Z);
            if (!sites.TryGetValue(key,out var old) || Compare(value,old) < 0) sites[key] = value;
            if (sites.Count > options.MaximumSites) throw new InvalidOperationException("TIN site limit exceeded. Increase the cell size.");
        }
        if (sites.Count < 3) throw new InvalidOperationException("TIN requires at least three distinct plan positions.");
        var vertices = sites.Values.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var lookup = vertices.Select((p,i) => (p,i)).ToDictionary(v => ((double)v.p.X,(double)v.p.Y),v => v.i);
        var envelope = new Envelope(); foreach (var p in vertices) envelope.ExpandToInclude(p.X,p.Y);
        // NTS requires a positive edge-coincidence tolerance to recognize points
        // exactly on existing edges. This is below every nonzero float spacing,
        // so distinct source XY values can never be merged by this tolerance.
        var subdivision = new QuadEdgeSubdivision(envelope,1e-80);
        var triangulator = new IncrementalDelaunayTriangulator(subdivision);
        // A stable shuffle avoids the quadratic insertion order of long sorted survey strips.
        var order = Enumerable.Range(0,vertices.Length).ToArray(); var random = new Random(1729);
        random.Shuffle(order);
        for (int i = 0; i < order.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var p = vertices[order[i]]; triangulator.InsertSite(new Vertex(p.X,p.Y,p.Z));
            if ((i & 127) == 0) progress?.Report(20 + (int)(60L*i/order.Length));
        }
        var faces = new List<int>(); var normals = new Vector3[vertices.Length];
        double maxSquared = options.MaximumEdge * options.MaximumEdge;
        foreach (var triangle in subdivision.GetTriangleCoordinates(false))
        {
            cancellation.ThrowIfCancellationRequested();
            int a = lookup[(triangle[0].X,triangle[0].Y)], b = lookup[(triangle[1].X,triangle[1].Y)], c = lookup[(triangle[2].X,triangle[2].Y)];
            var p = vertices[a]; var q = vertices[b]; var r = vertices[c];
            if (DistanceSquared(p,q) > maxSquared || DistanceSquared(q,r) > maxSquared || DistanceSquared(r,p) > maxSquared) continue;
            double ux = (double)q.X-p.X, uy = (double)q.Y-p.Y, uz = (double)q.Z-p.Z;
            double vx = (double)r.X-p.X, vy = (double)r.Y-p.Y, vz = (double)r.Z-p.Z;
            double nx = uy*vz-uz*vy, ny = uz*vx-ux*vz, nz = ux*vy-uy*vx;
            if (nz == 0) continue;
            if (nz < 0) { (b,c) = (c,b); nx = -nx; ny = -ny; nz = -nz; }
            double length = Math.Sqrt(nx*nx+ny*ny+nz*nz);
            var normal = new Vector3((float)(nx/length),(float)(ny/length),(float)(nz/length));
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
            faces.Add(a); faces.Add(b); faces.Add(c);
        }
        if (faces.Count == 0) throw new InvalidOperationException("TIN has no nondegenerate triangles within the maximum edge length.");
        for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].LengthSquared > 0 ? normals[i].Normalized() : Vector3.UnitZ;
        progress?.Report(100);
        return new(vertices,faces.ToArray(),origin.X,origin.Y,origin.Z) { Normals = normals };
    }

    private static int Compare(Vector3 a, Vector3 b)
    {
        int z = a.Z.CompareTo(b.Z); if (z != 0) return z;
        int x = a.X.CompareTo(b.X); return x != 0 ? x : a.Y.CompareTo(b.Y);
    }
    private static double DistanceSquared(Vector3 a, Vector3 b)
    { double x = (double)a.X-b.X, y = (double)a.Y-b.Y; return x*x+y*y; }
}
