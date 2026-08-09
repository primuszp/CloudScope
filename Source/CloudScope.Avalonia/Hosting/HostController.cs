using CloudScope.Commands;
using CloudScope.Loading;

namespace CloudScope.Avalonia.Hosting;

public sealed class HostController
{
    private readonly CommandRuntime _hostRuntime;
    private readonly CommandDispatcher _commands;
    private readonly DelegatingCommandExecutor _viewerCommands;

    private IEmbeddedOpenTkNativeHost? _embeddedHost;
    private int _renderedPointCount;
    private string _sourceName = "";
    private string _viewerState = "Embedded renderer not created";

    public event Action<string>? StatusChanged;

    public string StatusText => $"Points: {_renderedPointCount:N0} | {_viewerState}";
    public string CommandPrompt => _commands.CurrentPrompt;

    /// <summary>The command surface the command line completes against and submits to.</summary>
    public ICommandExecutor Commands => _commands;

    /// <summary>
    /// Live viewer state for the inspector and status bar. The shell owns the file name
    /// because the embedded viewer is loaded from a dataset, not from a path.
    /// </summary>
    public ViewerStatusSnapshot Status => (_embeddedHost?.Status ?? ViewerStatusSnapshot.Empty) with
    {
        SourceName = _sourceName
    };

    public event Action<ViewerStatusSnapshot>? ViewerStateChanged;

    /// <summary>Raises <see cref="ViewerStateChanged"/> so the shell can refresh bound UI.</summary>
    public void PublishViewerState() => ViewerStateChanged?.Invoke(Status);
    public IReadOnlyCollection<CloudScope.Labeling.LabelDefinition> LabelDefinitions =>
        _embeddedHost?.LabelDefinitions ?? System.Array.Empty<CloudScope.Labeling.LabelDefinition>();
    public string ActiveLabel => _embeddedHost?.ActiveLabel ?? "";
    public int? ActiveInstanceId => _embeddedHost?.ActiveInstanceId;

    public HostController()
    {
        _viewerCommands = new DelegatingCommandExecutor(
            () => _embeddedHost?.Commands,
            "Embedded renderer is not ready.");
        _hostRuntime = new CommandRuntime(this, new HostCommands(this));
        _commands = new CommandDispatcher(_viewerCommands.Execute);
        _commands.Register(_hostRuntime);
        _commands.Register(_viewerCommands);
    }

    public void SetEmbeddedHost(IEmbeddedOpenTkNativeHost embeddedHost)
    {
        _embeddedHost = embeddedHost;
        _viewerState = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? $"Embedded {embeddedHost.RendererName} host ready"
            : "Embedded renderer is currently implemented for Windows and macOS";
        PublishStatus();
    }

    public void SetPendingCloud(PointData[] points, int count, float radius, string sourceName)
    {
        _viewerState = $"Preparing spatial layout and uploading {count:N0} points: {sourceName}";
        PublishStatus();
        _embeddedHost?.LoadPointCloud(points, radius, () => CompleteCloudUpload(sourceName, count));
    }

    public void SetPendingCloud(PointCloudDataset dataset, string sourceName)
    {
        _sourceName = sourceName;
        _viewerState = $"Preparing spatial layout and uploading {dataset.LoadedCount:N0} points: {sourceName}";
        PublishStatus();
        _embeddedHost?.LoadPointCloud(dataset, () => CompleteCloudUpload(sourceName, dataset.LoadedCount));
    }

    public CommandResult ExecuteCommandResult(string commandText, bool publishResult = true)
    {
        CommandResult result = _commands.Execute(commandText);

        if (publishResult && !string.IsNullOrWhiteSpace(result.Message))
            StatusChanged?.Invoke(result.Message);

        return result;
    }

    public void ForwardKeyDown(ViewerKey key) => _embeddedHost?.ForwardKeyDown(key);

    public void ForwardKeyUp(ViewerKey key) => _embeddedHost?.ForwardKeyUp(key);

    public void FocusViewer() => _embeddedHost?.FocusViewer();

    internal void PerformReset()
    {
        _embeddedHost?.ResetViewer();
        _renderedPointCount = 0;
        _viewerState = _embeddedHost == null
            ? "Embedded renderer not created"
            : $"Embedded {_embeddedHost.RendererName} viewer reset";
        PublishStatus();
    }

    private void PublishStatus() => StatusChanged?.Invoke(StatusText);

    private void CompleteCloudUpload(string sourceName, int count)
    {
        _sourceName = sourceName;
        _renderedPointCount = count;
        PublishViewerState();
        string renderer = _embeddedHost?.RendererName ?? "GPU";
        _viewerState = $"Embedded {renderer} viewer: {sourceName}";
        StatusChanged?.Invoke($"Loaded into {renderer} viewer: {sourceName} ({count:N0} points)");
        PublishStatus();
    }
}
