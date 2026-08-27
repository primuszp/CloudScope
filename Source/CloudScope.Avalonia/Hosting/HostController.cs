using CloudScope.Commands;
using CloudScope.Loading;

namespace CloudScope.Avalonia.Hosting;

public sealed class HostController
{
    private readonly CommandRuntime _hostRuntime;
    private readonly CommandDispatcher _commands;
    private readonly DelegatingCommandExecutor _viewerCommands;

    private IEmbeddedViewerHost? _embeddedHost;

    private string _viewerState = "Embedded renderer not created";

    public event Action<string>? StatusChanged;

    public string StatusText => $"Points: {Status.LoadedCount:N0} | {_viewerState}";
    public string CommandPrompt => _commands.CurrentPrompt;

    /// <summary>The command surface the command line completes against and submits to.</summary>
    public ICommandExecutor Commands => _commands;

    /// <summary>
    /// Live viewer state for the inspector and status bar, straight from the viewer. The shell
    /// keeps no copy of it: the viewer opens its own clouds, so it is the one that knows.
    /// </summary>
    public ViewerStatusSnapshot Status => _embeddedHost?.Status ?? ViewerStatusSnapshot.Empty;

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

    /// <summary>
    /// Output the viewer produced on its own — a viewport pick answering a point prompt.
    /// The shell's command line shows it exactly like the answer to a typed prompt.
    /// </summary>
    public event Action<CommandResult>? ViewerCommandOutput;

    public void SetEmbeddedHost(IEmbeddedViewerHost embeddedHost)
    {
        _embeddedHost = embeddedHost;
        if (embeddedHost.Commands is ICommandOutputSource output)
            output.OutputProduced += result => ViewerCommandOutput?.Invoke(result);

        _viewerState = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? $"Embedded {embeddedHost.RendererName} host ready"
            : "Embedded renderer is currently implemented for Windows and macOS";
        PublishStatus();
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


    private void PublishStatus() => StatusChanged?.Invoke(StatusText);

}
