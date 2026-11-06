using CloudScope.Library;

namespace CloudScope.Loading;

/// <summary>The same format dispatch serves ordinary imports and project restoration.</summary>
public static class ResidentPointCloudReader
{
    public static LoadedPointCloud Load(string path, string format, long limit, IProgress<int>? progress, CancellationToken cancellation)
    {
        return format switch
        {
            "LAS" or "LAZ" => Las(path, limit, progress, cancellation),
            "PLY" => PlyPointCloudLoader.Load(path, limit, progress, cancellation),
            "XYZ" => XyzPointCloudLoader.Load(path, limit, progress, cancellation),
            "PTS" => PtsPointCloudLoader.Load(path, limit, progress, cancellation),
            "PTX" => PtxPointCloudLoader.Load(path, limit, progress, cancellation),
            "E57" => E57PointCloudLoader.Load(path, limit, progress, cancellation),
            _ => throw new InvalidDataException($"Unsupported resident source format: {format}.")
        };
    }
    private static LoadedPointCloud Las(string path, long limit, IProgress<int>? progress, CancellationToken cancellation)
    { using var reader = new LasReader(path); return PointCloudLoader.Load(reader, limit, progress, cancellation) with { SourceFormat = reader.IsCompressed ? "LAZ" : "LAS" }; }
}
