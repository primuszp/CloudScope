using CloudScope.Commands;
using CloudScope.Loading;

namespace CloudScope.Avalonia.Hosting;

public interface IEmbeddedOpenTkNativeHost
{
    string CommandPrompt { get; }
    void LoadPointCloud(PointData[] points, float radius);
    void LoadPointCloud(PointCloudDataset dataset);
    void ResetViewer();

    bool IsKnownCommand(string name);
    IReadOnlyCollection<string> KnownCommandNames { get; }
    bool HasActiveCommand { get; }
    bool IsTransparentCommand(string name);
    CommandResult CancelActiveCommand();
    CommandResult ExecuteViewerCommandResult(string commandText);
    string ExecuteViewerCommand(string commandText);

    void ForwardKeyDown(ViewerKey key);
    void ForwardKeyUp(ViewerKey key);
    void FocusViewer();
}
