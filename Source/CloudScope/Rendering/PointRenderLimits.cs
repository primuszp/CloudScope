namespace CloudScope.Rendering;

/// <summary>
/// The two ceilings that keep a large cloud within the hardware: how many points may live in
/// GPU memory, and how many may be drawn in one frame.
/// </summary>
/// <remarks>
/// Density is chosen per octree cell by <see cref="PointLodPlanner"/> from what the screen can
/// actually show; these limits only put a roof over that decision, so the frame cost of a
/// hundred-million-point cloud is the same as a ten-million-point one. Both are overridable at
/// runtime, either for all backends (<c>CLOUDSCOPE_MAX_RESIDENT_POINTS</c>,
/// <c>CLOUDSCOPE_MAX_DRAW_POINTS</c>) or for one (<c>CLOUDSCOPE_METAL_MAX_DRAW_POINTS</c>,
/// <c>CLOUDSCOPE_OPENGL_MAX_DRAW_POINTS</c>, …), which is also how the two backends are
/// compared under identical conditions.
/// </remarks>
internal readonly record struct PointRenderLimits(int MaxResidentPoints, int MaxDrawPointsPerFrame)
{
    /// <summary>
    /// Default per-frame ceiling. Beyond a few million point sprites the frame is bound by
    /// raster throughput on any GPU, and the extra points only overdraw each other.
    /// </summary>
    private const int DefaultMaxDrawPointsPerFrame = 6_000_000;

    /// <summary>Share of the GPU's working set a cloud may occupy when the backend reports one.</summary>
    private const double GpuMemoryShare = 0.5;

    public static PointRenderLimits Load(string backendName) => new(
        ReadLimit("CLOUDSCOPE_MAX_RESIDENT_POINTS", $"CLOUDSCOPE_{backendName}_MAX_RESIDENT_POINTS", int.MaxValue),
        ReadLimit("CLOUDSCOPE_MAX_DRAW_POINTS", $"CLOUDSCOPE_{backendName}_MAX_DRAW_POINTS", DefaultMaxDrawPointsPerFrame));

    /// <summary>
    /// The resident ceiling for a backend that can report its memory, in points.
    /// </summary>
    /// <param name="availableGpuBytes">
    /// What the device recommends keeping resident, or null when the API cannot say — OpenGL
    /// has no portable query, so there the cloud is only bounded by the environment override.
    /// </param>
    /// <param name="bytesPerPoint">Resident cost of one point, vertex plus attributes.</param>
    public int ResolveResidentLimit(long? availableGpuBytes, int bytesPerPoint)
    {
        if (MaxResidentPoints != int.MaxValue || availableGpuBytes is not > 0 || bytesPerPoint <= 0)
            return MaxResidentPoints;

        // Half the working set: the rest is the frame's own attachments, the highlight
        // buffers, and whatever else shares the device.
        long affordable = (long)(availableGpuBytes.Value * GpuMemoryShare) / bytesPerPoint;
        return (int)Math.Clamp(affordable, 1_000_000, int.MaxValue);
    }

    /// <summary>The per-frame point budget for a cloud of <paramref name="pointCount"/> points.</summary>
    public int GetFrameBudget(int pointCount) => Math.Min(pointCount, MaxDrawPointsPerFrame);

    private static int ReadLimit(string commonVariable, string backendVariable, int defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(backendVariable)
            ?? Environment.GetEnvironmentVariable(commonVariable);
        return int.TryParse(value, out int limit) && limit > 0 ? limit : defaultValue;
    }
}
