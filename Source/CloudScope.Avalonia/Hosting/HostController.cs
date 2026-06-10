using CloudScope.Commands;

namespace CloudScope.Avalonia.Hosting;

public sealed class HostController
{
    private readonly CommandRuntime _hostRuntime;
    private IEmbeddedOpenTkNativeHost? _embeddedHost;
    private int _renderedPointCount;
    private string _viewerState = "Embedded OpenTK host not created";

    public event Action<string>? StatusChanged;

    public string Status => $"Points: {_renderedPointCount:N0} | {_viewerState}";
    public string CommandPrompt => _embeddedHost?.CommandPrompt ?? "Command:";

    public HostController()
    {
        _hostRuntime = new CommandRuntime(this, new HostCommands(this));
    }

    public void SetEmbeddedHost(IEmbeddedOpenTkNativeHost embeddedHost)
    {
        _embeddedHost = embeddedHost;
        _viewerState = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? "Embedded OpenTK host ready"
            : "Embedded OpenTK host is currently implemented for Windows and macOS";
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

    public CommandResult ExecuteCommandResult(string commandText, bool publishResult = true)
    {
        string trimmed = commandText.Trim();
        string firstWord = trimmed.Length == 0 ? "" : trimmed.Split(' ', 2)[0];

        CommandResult result = _hostRuntime.IsKnownCommand(firstWord)
            ? _hostRuntime.Execute(trimmed)
            : EmbeddedExecuteResult(trimmed);

        if (publishResult && !string.IsNullOrWhiteSpace(result.Message))
            StatusChanged?.Invoke(result.Message);

        return result;
    }

    public string ExecuteCommand(string commandText, bool publishResult = true) =>
        ExecuteCommandResult(commandText, publishResult).Message;

    public void ForwardKeyDown(ViewerKey key) => _embeddedHost?.ForwardKeyDown(key);

    public void ForwardKeyUp(ViewerKey key) => _embeddedHost?.ForwardKeyUp(key);

    public void FocusViewer() => _embeddedHost?.FocusViewer();

    internal void PerformReset()
    {
        _renderedPointCount = 0;
        _viewerState = "Embedded OpenTK host not created";
        PublishStatus();
    }

    private CommandResult EmbeddedExecuteResult(string commandText) =>
        _embeddedHost?.ExecuteViewerCommandResult(commandText)
        ?? CommandResult.End("Embedded OpenTK host is not ready.");

    private void PublishStatus() => StatusChanged?.Invoke(Status);
}
