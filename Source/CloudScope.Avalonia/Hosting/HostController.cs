using CloudScope.Commands;

namespace CloudScope.Avalonia.Hosting;

public sealed class HostController
{
    private readonly CommandRuntime _hostRuntime;
    private readonly CommandDispatcher _commands;
    private IEmbeddedOpenTkNativeHost? _embeddedHost;
    private int _renderedPointCount;
    private string _viewerState = "Embedded OpenTK host not created";

    public event Action<string>? StatusChanged;

    public string Status => $"Points: {_renderedPointCount:N0} | {_viewerState}";
    public string CommandPrompt => _commands.CurrentPrompt;

    public HostController()
    {
        _hostRuntime = new CommandRuntime(this, new HostCommands(this));
        _commands = new CommandDispatcher(EmbeddedExecuteResult);
        _commands.Register(_hostRuntime);
        _commands.Register(new EmbeddedCommandExecutor(this));
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
        CommandResult result = _commands.Execute(commandText);

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
        _embeddedHost?.ResetViewer();
        _renderedPointCount = 0;
        _viewerState = _embeddedHost == null
            ? "Embedded OpenTK host not created"
            : "Embedded OpenTK viewer reset";
        PublishStatus();
    }

    private CommandResult EmbeddedExecuteResult(string commandText) =>
        _embeddedHost?.ExecuteViewerCommandResult(commandText)
        ?? CommandResult.End("Embedded OpenTK host is not ready.");

    private void PublishStatus() => StatusChanged?.Invoke(Status);

    private sealed class EmbeddedCommandExecutor(HostController host) : ICommandExecutor
    {
        public string CurrentPrompt => host._embeddedHost?.CommandPrompt ?? "Command:";

        public bool IsKnownCommand(string name) => host._embeddedHost?.IsKnownCommand(name) == true;

        public CommandResult Execute(string input) => host.EmbeddedExecuteResult(input);
    }
}
