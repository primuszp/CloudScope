namespace CloudScope.Avalonia;

public sealed class HostController
{
    private DateTime _startTimeUtc = DateTime.UtcNow;
    private Win32EmbeddedOpenTkNativeHost? _embeddedHost;
    private int _renderedPointCount;
    private string _viewerState = "Embedded OpenTK host not created";

    public event Action<string>? StatusChanged;

    public string Status => $"Points: {_renderedPointCount:N0} | {_viewerState}";

    public void SetEmbeddedHost(Win32EmbeddedOpenTkNativeHost embeddedHost)
    {
        _embeddedHost = embeddedHost;
        _viewerState = OperatingSystem.IsWindows()
            ? "Embedded OpenTK host ready"
            : "Embedded OpenTK host is currently Windows-only";
        PublishStatus();
    }

    public void SetPendingCloud(PointData[] points, int count, float radius, string sourceName)
    {
        _embeddedHost?.LoadPointCloud(points, radius);
        _renderedPointCount = count;
        _viewerState = $"Embedded OpenTK viewer: {sourceName}";

        StatusChanged?.Invoke($"Loaded into OpenTK viewer: {sourceName} ({count:N0} points)");
        PublishStatus();
    }

    public string ExecuteCommand(string commandText)
    {
        string trimmed = commandText.Trim();
        string command = trimmed.ToUpperInvariant();
        string result = command switch
        {
            "" => "",
            "STATUS" => Status,
            "RESET" => Reset(),
            "HOSTHELP" => "Host commands: STATUS, RESET, HOSTHELP.",
            _ => _embeddedHost?.ExecuteViewerCommand(trimmed) ?? "Embedded OpenTK host is not ready."
        };

        if (result.Length > 0)
            StatusChanged?.Invoke(result);

        return result;
    }

    public void ForwardKeyDown(ViewerKey key) => _embeddedHost?.ForwardKeyDown(key);

    public void ForwardKeyUp(ViewerKey key) => _embeddedHost?.ForwardKeyUp(key);

    public void FocusViewer() => _embeddedHost?.FocusViewer();

    private string Reset()
    {
        _startTimeUtc = DateTime.UtcNow;
        _renderedPointCount = 0;
        PublishStatus();
        return "Host controller reset";
    }

    private void PublishStatus() => StatusChanged?.Invoke(Status);

}
