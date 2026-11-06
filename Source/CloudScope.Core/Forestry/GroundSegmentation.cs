namespace CloudScope.Forestry;

/// <summary>
/// Parameters of the Cloth Simulation Filter (CSF), after Zhang et al.'s open-source
/// implementation (<see href="https://github.com/jianboqi/CSF"/>; Apache-2.0). Values and
/// defaults match its <c>Params</c> structure, with names corrected for the public C# API.
/// </summary>
public sealed record GroundSegmentationOptions
{
    public float ClothResolution { get; init; } = 1f;
    public int Rigidness { get; init; } = 3;
    public double TimeStep { get; init; } = 0.65;
    public float ClassThreshold { get; init; } = 0.5f;
    public int Iterations { get; init; } = 500;
    public bool SlopeSmoothing { get; init; } = true;
}

public sealed record GroundSegmentationResult(
    IReadOnlyList<int> PointIndices,
    int ClothNodeCount,
    int ContactNodeCount,
    int NonGroundPointCount,
    string? FailureReason = null)
{
    public bool Succeeded => FailureReason is null && PointIndices.Count > 0;
}

/// <summary>
/// Managed implementation of the CSF cloth-simulation ground filter. The cloud is inverted,
/// a constrained cloth falls onto its rasterized surface, and each point is classified by its
/// interpolated distance from that cloth. It is based on the Apache-2.0 CSF reference project
/// by Zhang et al.; the managed representation avoids a native dependency in every shell.
/// </summary>
public static class GroundSegmentation
{
    private const int ClothBuffer = 2;
    private const int MaximumClothNodes = 1_000_000;
    private const double Gravity = 0.2;
    private const double Damping = 0.01;
    private const double Convergence = 0.005;
    private const double SlopeSmoothThreshold = 0.3;
    private static readonly double[] SingleMove = [0, .3, .51, .657, .7599, .83193, .88235, .91765, .94235, .95965, .97175, .98023, .98616, .99031, .99322];
    private static readonly double[] DoubleMove = [0, .3, .42, .468, .4872, .4949, .498, .4992, .4997, .4999, .4999, .5, .5, .5, .5];

    public static GroundSegmentationResult Segment(
        IReadOnlyList<PointData> points, GroundSegmentationOptions? options = null,
        CancellationToken cancellation = default, IProgress<int>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        progress?.Report(0);
        options ??= new GroundSegmentationOptions();
        if (points.Count == 0)
            return Failed("No resident point cloud is loaded.");
        if (!TryValidate(options, out string error))
            return Failed(error);

        float resolution = options.ClothResolution;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (PointData point in points)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                return Failed("CSF requires finite point coordinates.");
            minX = MathF.Min(minX, point.X); maxX = MathF.Max(maxX, point.X);
            minY = MathF.Min(minY, point.Y); maxY = MathF.Max(maxY, point.Y);
            minZ = MathF.Min(minZ, point.Z);
        }

        // Validate in double before converting: overflowing an integer grid dimension can
        // otherwise wrap back into a small, apparently valid allocation.
        double columns = Math.Floor(((double)maxX - minX) / resolution) + 2 * ClothBuffer;
        double rows = Math.Floor(((double)maxY - minY) / resolution) + 2 * ClothBuffer;
        if (columns < 2 || rows < 2 || columns * rows > MaximumClothNodes)
            return Failed($"The CSF cloth would have {columns:N0} × {rows:N0} nodes. Increase ClothResolution to keep it below {MaximumClothNodes:N0} nodes.");
        int width = (int)columns, height = (int)rows;

        int nodeCount = checked(width * height);
        float originX = minX - ClothBuffer * resolution;
        float originY = minY - ClothBuffer * resolution;
        double startHeight = -minZ + 0.05;
        var terrain = new double[nodeCount];
        var nearestDistance = new double[nodeCount];
        Array.Fill(terrain, double.NaN);
        Array.Fill(nearestDistance, double.PositiveInfinity);

        // CSF rasterizes the inverted point cloud. Keep the original X/Y ground plane and
        // store -Z as the collision height; this is equivalent to CSF::setPointCloud.
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            if ((pointIndex & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            PointData point = points[pointIndex];
            int x = Math.Clamp((int)(((point.X - originX) / resolution) + 0.5f), 0, width - 1);
            int y = Math.Clamp((int)(((point.Y - originY) / resolution) + 0.5f), 0, height - 1);
            int node = Index(x, y, width);
            double dx = point.X - (originX + x * resolution);
            double dy = point.Y - (originY + y * resolution);
            double distance = dx * dx + dy * dy;
            // Equal XY distances must not make classification depend on source order.
            // The lower normal-Z return is the upper collision surface in inverted Z.
            if (distance > nearestDistance[node]
                || distance == nearestDistance[node] && !double.IsNaN(terrain[node]) && -point.Z <= terrain[node])
                continue;

            nearestDistance[node] = distance;
            terrain[node] = -point.Z;
        }

        if (!FillEmptyTerrainNodes(terrain, width, height, cancellation))
            return Failed("CSF could not rasterize the point cloud.");

        var position = new double[nodeCount];
        var previous = new double[nodeCount];
        var movable = new bool[nodeCount];
        Array.Fill(position, startHeight);
        Array.Fill(previous, startHeight);
        Array.Fill(movable, true);

        double acceleration = -Gravity * options.TimeStep * options.TimeStep;
        for (int iteration = 0; iteration < options.Iterations; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(10 + 75 * iteration / options.Iterations);
            double maxDifference = Advance(position, previous, movable, acceleration, options.TimeStep, cancellation);
            RelaxConstraints(position, movable, width, height, options.Rigidness, cancellation);
            CollideWithTerrain(position, movable, terrain, cancellation);
            if (maxDifference > 0 && maxDifference < Convergence)
                break;
        }

        if (options.SlopeSmoothing)
            SmoothSlopes(position, movable, terrain, width, height, cancellation);

        progress?.Report(90);
        int contacts = movable.Count(node => !node);
        var ground = new List<int>();
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            if ((pointIndex & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            PointData point = points[pointIndex];
            double cloth = Interpolate(position, point.X, point.Y, originX, originY, resolution, width, height);
            if (Math.Abs(cloth + point.Z) < options.ClassThreshold)
                ground.Add(pointIndex);
        }

        cancellation.ThrowIfCancellationRequested();
        progress?.Report(100);
        return ground.Count == 0
            ? new([], nodeCount, contacts, points.Count, "No points are within the CSF classification threshold of the cloth.")
            : new(ground, nodeCount, contacts, points.Count - ground.Count);
    }

    private static bool TryValidate(GroundSegmentationOptions options, out string error)
    {
        if (!float.IsFinite(options.ClothResolution) || options.ClothResolution is < 0.05f or > 1000f)
            error = "ClothResolution must be between 0.05 and 1000.";
        else if (options.Rigidness is < 1 or > 15)
            error = "Rigidness must be between 1 and 15.";
        else if (!double.IsFinite(options.TimeStep) || options.TimeStep is < 0.05 or > 2)
            error = "TimeStep must be between 0.05 and 2.";
        else if (!float.IsFinite(options.ClassThreshold) || options.ClassThreshold is < 0.01f or > 100f)
            error = "ClassThreshold must be between 0.01 and 100.";
        else if (options.Iterations is < 1 or > 5000)
            error = "Iterations must be between 1 and 5000.";
        else
        {
            error = "";
            return true;
        }

        return false;
    }

    private static GroundSegmentationResult Failed(string reason) => new([], 0, 0, 0, reason);

    private static double Advance(double[] position, double[] previous, bool[] movable, double acceleration, double timeStep, CancellationToken cancellation)
    {
        double timeStepSquared = timeStep * timeStep;
        double maximum = 0;
        for (int index = 0; index < position.Length; index++)
        {
            if ((index & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            if (!movable[index])
                continue;

            double oldPosition = position[index];
            position[index] += (position[index] - previous[index]) * (1 - Damping) + acceleration * timeStepSquared;
            previous[index] = oldPosition;
            maximum = Math.Max(maximum, Math.Abs(oldPosition - position[index]));
        }

        return maximum;
    }

    private static void RelaxConstraints(double[] position, bool[] movable, int width, int height, int rigidness, CancellationToken cancellation)
    {
        double single = rigidness > 14 ? 1 : SingleMove[rigidness];
        double paired = rigidness > 14 ? .5 : DoubleMove[rigidness];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if ((x & 255) == 0) cancellation.ThrowIfCancellationRequested();
                int node = Index(x, y, width);
                Relax(node, x + 1, y, width, height, position, movable, single, paired);
                Relax(node, x, y + 1, width, height, position, movable, single, paired);
                Relax(node, x + 1, y + 1, width, height, position, movable, single, paired);
                Relax(node, x - 1, y + 1, width, height, position, movable, single, paired);
                Relax(node, x + 2, y, width, height, position, movable, single, paired);
                Relax(node, x, y + 2, width, height, position, movable, single, paired);
                Relax(node, x + 2, y + 2, width, height, position, movable, single, paired);
                Relax(node, x - 2, y + 2, width, height, position, movable, single, paired);
            }
    }

    private static void Relax(int first, int x, int y, int width, int height, double[] position, bool[] movable, double single, double paired)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;

        int second = Index(x, y, width);
        double correction = position[second] - position[first];
        if (movable[first] && movable[second])
        {
            double offset = correction * paired;
            position[first] += offset;
            position[second] -= offset;
        }
        else if (movable[first])
        {
            position[first] += correction * single;
        }
        else if (movable[second])
        {
            position[second] -= correction * single;
        }
    }

    private static void CollideWithTerrain(double[] position, bool[] movable, double[] terrain, CancellationToken cancellation)
    {
        for (int index = 0; index < position.Length; index++)
        {
            if ((index & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            if (movable[index] && position[index] < terrain[index])
            {
                position[index] = terrain[index];
                movable[index] = false;
            }
        }
    }

    private static void SmoothSlopes(double[] position, bool[] movable, double[] terrain, int width, int height, CancellationToken cancellation)
    {
        var visited = new bool[position.Length];
        for (int start = 0; start < position.Length; start++)
        {
            if ((start & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            if (!movable[start] || visited[start])
                continue;

            List<int> component = ConnectedMovableNodes(start, movable, visited, width, height, cancellation);
            if (component.Count <= 50)
                continue;

            var queue = new Queue<int>();
            foreach (int node in component)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!TouchesCompatibleContact(node, movable, terrain, width, height))
                    continue;

                position[node] = terrain[node];
                movable[node] = false;
                queue.Enqueue(node);
            }

            while (queue.TryDequeue(out int node))
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (int neighbour in Neighbours4(node, width, height))
                {
                    if (!movable[neighbour] || Math.Abs(terrain[node] - terrain[neighbour]) >= SlopeSmoothThreshold)
                        continue;

                    position[neighbour] = terrain[neighbour];
                    movable[neighbour] = false;
                    queue.Enqueue(neighbour);
                }
            }
        }
    }

    private static List<int> ConnectedMovableNodes(int start, bool[] movable, bool[] visited, int width, int height, CancellationToken cancellation)
    {
        var component = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(start);
        visited[start] = true;
        while (queue.TryDequeue(out int node))
        {
            cancellation.ThrowIfCancellationRequested();
            component.Add(node);
            foreach (int neighbour in Neighbours4(node, width, height))
            {
                if (movable[neighbour] && !visited[neighbour])
                {
                    visited[neighbour] = true;
                    queue.Enqueue(neighbour);
                }
            }
        }

        return component;
    }

    private static bool TouchesCompatibleContact(int node, bool[] movable, double[] terrain, int width, int height) =>
        Neighbours4(node, width, height).Any(neighbour =>
            !movable[neighbour] && Math.Abs(terrain[node] - terrain[neighbour]) < SlopeSmoothThreshold);

    private static IEnumerable<int> Neighbours4(int node, int width, int height)
    {
        int x = node % width;
        int y = node / width;
        if (x > 0) yield return node - 1;
        if (x + 1 < width) yield return node + 1;
        if (y > 0) yield return node - width;
        if (y + 1 < height) yield return node + width;
    }

    private static bool FillEmptyTerrainNodes(double[] terrain, int width, int height, CancellationToken cancellation)
    {
        var queue = new Queue<int>();
        for (int index = 0; index < terrain.Length; index++)
            if (!double.IsNaN(terrain[index])) queue.Enqueue(index);
        if (queue.Count == 0)
            return false;

        while (queue.TryDequeue(out int node))
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (int neighbour in Neighbours4(node, width, height))
            {
                if (!double.IsNaN(terrain[neighbour]))
                    continue;

                terrain[neighbour] = terrain[node];
                queue.Enqueue(neighbour);
            }
        }

        return true;
    }

    private static double Interpolate(double[] cloth, float x, float y, float originX, float originY, float resolution, int width, int height)
    {
        double gridX = (x - originX) / resolution;
        double gridY = (y - originY) / resolution;
        int x0 = Math.Clamp((int)Math.Floor(gridX), 0, width - 2);
        int y0 = Math.Clamp((int)Math.Floor(gridY), 0, height - 2);
        double dx = Math.Clamp(gridX - x0, 0, 1);
        double dy = Math.Clamp(gridY - y0, 0, 1);
        return cloth[Index(x0, y0, width)] * (1 - dx) * (1 - dy)
             + cloth[Index(x0, y0 + 1, width)] * (1 - dx) * dy
             + cloth[Index(x0 + 1, y0 + 1, width)] * dx * dy
             + cloth[Index(x0 + 1, y0, width)] * dx * (1 - dy);
    }

    private static int Index(int x, int y, int width) => y * width + x;
}
