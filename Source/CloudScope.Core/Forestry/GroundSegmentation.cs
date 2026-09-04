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
        IReadOnlyList<PointData> points, GroundSegmentationOptions? options = null)
    {
        options ??= new GroundSegmentationOptions();
        if (points.Count == 0)
            return Failed("No resident point cloud is loaded.");
        if (!TryValidate(options, out string error))
            return Failed(error);

        float resolution = options.ClothResolution;
        float minX = points.Min(point => point.X);
        float maxX = points.Max(point => point.X);
        float minY = points.Min(point => point.Y);
        float maxY = points.Max(point => point.Y);
        float minZ = points.Min(point => point.Z);

        int width = (int)MathF.Floor((maxX - minX) / resolution) + 2 * ClothBuffer;
        int height = (int)MathF.Floor((maxY - minY) / resolution) + 2 * ClothBuffer;
        if (width < 2 || height < 2 || (long)width * height > MaximumClothNodes)
            return Failed($"The CSF cloth would have {width:N0} × {height:N0} nodes. Increase ClothResolution to keep it below {MaximumClothNodes:N0} nodes.");

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
            PointData point = points[pointIndex];
            int x = Math.Clamp((int)(((point.X - originX) / resolution) + 0.5f), 0, width - 1);
            int y = Math.Clamp((int)(((point.Y - originY) / resolution) + 0.5f), 0, height - 1);
            int node = Index(x, y, width);
            double dx = point.X - (originX + x * resolution);
            double dy = point.Y - (originY + y * resolution);
            double distance = dx * dx + dy * dy;
            if (distance >= nearestDistance[node])
                continue;

            nearestDistance[node] = distance;
            terrain[node] = -point.Z;
        }

        if (!FillEmptyTerrainNodes(terrain, width, height))
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
            double maxDifference = Advance(position, previous, movable, acceleration, options.TimeStep);
            RelaxConstraints(position, movable, width, height, options.Rigidness);
            CollideWithTerrain(position, movable, terrain);
            if (maxDifference > 0 && maxDifference < Convergence)
                break;
        }

        if (options.SlopeSmoothing)
            SmoothSlopes(position, movable, terrain, width, height);

        int contacts = movable.Count(node => !node);
        var ground = new List<int>();
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            PointData point = points[pointIndex];
            double cloth = Interpolate(position, point.X, point.Y, originX, originY, resolution, width, height);
            if (Math.Abs(cloth + point.Z) < options.ClassThreshold)
                ground.Add(pointIndex);
        }

        return ground.Count == 0
            ? new([], nodeCount, contacts, points.Count, "No points are within the CSF classification threshold of the cloth.")
            : new(ground, nodeCount, contacts, points.Count - ground.Count);
    }

    private static bool TryValidate(GroundSegmentationOptions options, out string error)
    {
        if (options.ClothResolution is < 0.05f or > 1000f)
            error = "ClothResolution must be between 0.05 and 1000.";
        else if (options.Rigidness is < 1 or > 15)
            error = "Rigidness must be between 1 and 15.";
        else if (options.TimeStep is < 0.05 or > 2)
            error = "TimeStep must be between 0.05 and 2.";
        else if (options.ClassThreshold is < 0.01f or > 100f)
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

    private static double Advance(double[] position, double[] previous, bool[] movable, double acceleration, double timeStep)
    {
        double timeStepSquared = timeStep * timeStep;
        double maximum = 0;
        for (int index = 0; index < position.Length; index++)
        {
            if (!movable[index])
                continue;

            double oldPosition = position[index];
            position[index] += (position[index] - previous[index]) * (1 - Damping) + acceleration * timeStepSquared;
            previous[index] = oldPosition;
            maximum = Math.Max(maximum, Math.Abs(oldPosition - position[index]));
        }

        return maximum;
    }

    private static void RelaxConstraints(double[] position, bool[] movable, int width, int height, int rigidness)
    {
        double single = rigidness > 14 ? 1 : SingleMove[rigidness];
        double paired = rigidness > 14 ? .5 : DoubleMove[rigidness];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
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

    private static void CollideWithTerrain(double[] position, bool[] movable, double[] terrain)
    {
        for (int index = 0; index < position.Length; index++)
        {
            if (movable[index] && position[index] < terrain[index])
            {
                position[index] = terrain[index];
                movable[index] = false;
            }
        }
    }

    private static void SmoothSlopes(double[] position, bool[] movable, double[] terrain, int width, int height)
    {
        var visited = new bool[position.Length];
        for (int start = 0; start < position.Length; start++)
        {
            if (!movable[start] || visited[start])
                continue;

            List<int> component = ConnectedMovableNodes(start, movable, visited, width, height);
            if (component.Count <= 50)
                continue;

            var queue = new Queue<int>();
            foreach (int node in component)
            {
                if (!TouchesCompatibleContact(node, movable, terrain, width, height))
                    continue;

                position[node] = terrain[node];
                movable[node] = false;
                queue.Enqueue(node);
            }

            while (queue.TryDequeue(out int node))
            {
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

    private static List<int> ConnectedMovableNodes(int start, bool[] movable, bool[] visited, int width, int height)
    {
        var component = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(start);
        visited[start] = true;
        while (queue.TryDequeue(out int node))
        {
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

    private static bool FillEmptyTerrainNodes(double[] terrain, int width, int height)
    {
        var queue = new Queue<int>();
        for (int index = 0; index < terrain.Length; index++)
            if (!double.IsNaN(terrain[index])) queue.Enqueue(index);
        if (queue.Count == 0)
            return false;

        while (queue.TryDequeue(out int node))
        {
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
