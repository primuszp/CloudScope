namespace CloudScope.Loading;

/// <summary>Conservative path comparison for protecting source files during atomic export.</summary>
public static class FilePathIdentity
{
    public static bool SamePath(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
