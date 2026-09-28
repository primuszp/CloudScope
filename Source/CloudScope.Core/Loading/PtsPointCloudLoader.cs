namespace CloudScope.Loading;

/// <summary>Reads PTS text points with an optional count header and signed scanner intensity.</summary>
public static class PtsPointCloudLoader
{
    public static LoadedPointCloud Load(string path, long maxPoints = 0, IProgress<int>? progress = null)
        => XyzPointCloudLoader.LoadText(path, maxPoints, progress, pts: true);
}
