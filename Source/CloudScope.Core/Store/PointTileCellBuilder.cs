using System;
using System.Collections.Generic;

namespace CloudScope.Store;

/// <summary>One finished octree cell, before it is given its place in the store.</summary>
internal sealed class PointTileCell
{
    public required StorePoint[] Points { get; init; }
    public required int PointCount { get; init; }
    public required (float Min, float Max)[] Cube { get; init; }
    public required byte Level { get; init; }

    /// <summary>Indices into the chunk's own cell list; rebased when the chunk is appended.</summary>
    public List<int> Children { get; } = [];
}

/// <summary>
/// Turns one chunk's points into octree cells by additive sampling.
/// </summary>
/// <remarks>
/// At each cell the space is divided into a grid, and a point is kept by the cell when its
/// square is still free; the rest are handed down to the octant they fall in and the same
/// rule is applied one level deeper. So a cell holds an even, grid-spaced sample of
/// everything beneath it, its children hold what the grid could not fit, and no point is
/// stored twice. Drawing a cell together with its ancestors is then an even sample of the
/// region at whatever depth the traversal reached - which is what lets the renderer stop
/// descending as soon as the screen cannot show more detail.
/// </remarks>
internal static class PointTileCellBuilder
{
    public static void Build(
        StorePoint[] points,
        int count,
        (float Min, float Max)[] cube,
        int level,
        PointTileBuildOptions options,
        List<PointTileCell> cells)
    {
        if (count <= 0)
            return;

        BuildCell(points.AsSpan(0, count).ToArray(), cube, (byte)level, options, cells);
    }

    private static int BuildCell(
        StorePoint[] points,
        (float Min, float Max)[] cube,
        byte level,
        PointTileBuildOptions options,
        List<PointTileCell> cells)
    {
        // Small enough to draw in one go: keep everything and stop splitting.
        if (points.Length <= options.MinimumCellPoints || level >= byte.MaxValue - 1)
        {
            cells.Add(new PointTileCell
            {
                Points = points,
                PointCount = points.Length,
                Cube = cube,
                Level = level
            });
            return cells.Count - 1;
        }

        int resolution = options.CellGridResolution;
        var occupied = new bool[resolution * resolution * resolution];
        var kept = new List<StorePoint>(Math.Min(points.Length, occupied.Length));
        var octants = new List<StorePoint>[8];

        float sizeX = MathF.Max(cube[0].Max - cube[0].Min, 1e-6f);
        float sizeY = MathF.Max(cube[1].Max - cube[1].Min, 1e-6f);
        float sizeZ = MathF.Max(cube[2].Max - cube[2].Min, 1e-6f);
        float centreX = (cube[0].Min + cube[0].Max) * 0.5f;
        float centreY = (cube[1].Min + cube[1].Max) * 0.5f;
        float centreZ = (cube[2].Min + cube[2].Max) * 0.5f;

        foreach (StorePoint point in points)
        {
            int gx = Axis(point.Vertex.X, cube[0].Min, sizeX, resolution);
            int gy = Axis(point.Vertex.Y, cube[1].Min, sizeY, resolution);
            int gz = Axis(point.Vertex.Z, cube[2].Min, sizeZ, resolution);
            int cell = gx + resolution * (gy + resolution * gz);

            if (!occupied[cell])
            {
                occupied[cell] = true;
                kept.Add(point);
                continue;
            }

            int octant = (point.Vertex.X >= centreX ? 1 : 0)
                | (point.Vertex.Y >= centreY ? 2 : 0)
                | (point.Vertex.Z >= centreZ ? 4 : 0);
            (octants[octant] ??= []).Add(point);
        }

        var self = new PointTileCell
        {
            Points = kept.ToArray(),
            PointCount = kept.Count,
            Cube = cube,
            Level = level
        };
        cells.Add(self);
        int selfIndex = cells.Count - 1;

        for (int octant = 0; octant < 8; octant++)
        {
            List<StorePoint>? childPoints = octants[octant];
            if (childPoints is not { Count: > 0 })
                continue;

            self.Children.Add(BuildCell(
                childPoints.ToArray(), OctantCube(cube, octant), (byte)(level + 1), options, cells));
        }

        return selfIndex;
    }

    private static int Axis(float value, float min, float size, int resolution)
    {
        int index = (int)((value - min) / size * resolution);
        return Math.Clamp(index, 0, resolution - 1);
    }

    private static (float Min, float Max)[] OctantCube((float Min, float Max)[] cube, int octant)
    {
        var child = new (float Min, float Max)[3];
        for (int axis = 0; axis < 3; axis++)
        {
            float centre = (cube[axis].Min + cube[axis].Max) * 0.5f;
            bool upper = (octant & (1 << axis)) != 0;
            child[axis] = upper ? (centre, cube[axis].Max) : (cube[axis].Min, centre);
        }

        return child;
    }
}
