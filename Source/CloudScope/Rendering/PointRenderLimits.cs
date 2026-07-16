namespace CloudScope.Rendering;

internal readonly record struct PointRenderLimits(int MaxResidentPoints, int MaxDrawPointsPerFrame)
{
    public static PointRenderLimits Load(string backendName) => new(
        ReadLimit("CLOUDSCOPE_MAX_RESIDENT_POINTS", $"CLOUDSCOPE_{backendName}_MAX_RESIDENT_POINTS", int.MaxValue),
        ReadLimit("CLOUDSCOPE_MAX_DRAW_POINTS", $"CLOUDSCOPE_{backendName}_MAX_DRAW_POINTS", int.MaxValue));

    private static int ReadLimit(string commonVariable, string backendVariable, int defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(backendVariable)
            ?? Environment.GetEnvironmentVariable(commonVariable);
        return int.TryParse(value, out int limit) && limit > 0 ? limit : defaultValue;
    }
}
