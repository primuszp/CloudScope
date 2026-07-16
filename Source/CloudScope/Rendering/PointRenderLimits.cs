namespace CloudScope.Rendering;

internal readonly record struct PointRenderLimits(int MaxResidentPoints, int MaxDrawPointsPerFrame)
{
    private const int DefaultLimit = 5_000_000;

    public static PointRenderLimits Load(string backendName) => new(
        ReadLimit("CLOUDSCOPE_MAX_RESIDENT_POINTS", $"CLOUDSCOPE_{backendName}_MAX_RESIDENT_POINTS"),
        ReadLimit("CLOUDSCOPE_MAX_DRAW_POINTS", $"CLOUDSCOPE_{backendName}_MAX_DRAW_POINTS"));

    private static int ReadLimit(string commonVariable, string backendVariable)
    {
        string? value = Environment.GetEnvironmentVariable(backendVariable)
            ?? Environment.GetEnvironmentVariable(commonVariable);
        return int.TryParse(value, out int limit) && limit > 0 ? limit : DefaultLimit;
    }
}
