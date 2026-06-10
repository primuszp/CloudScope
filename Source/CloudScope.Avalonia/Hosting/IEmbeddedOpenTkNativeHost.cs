using CloudScope.Commands;

namespace CloudScope.Avalonia.Hosting;

public interface IEmbeddedOpenTkNativeHost
{
    string CommandPrompt { get; }
    void LoadPointCloud(PointData[] points, float radius);

    CommandResult ExecuteViewerCommandResult(string commandText);
    string ExecuteViewerCommand(string commandText);

    void ForwardKeyDown(ViewerKey key);
    void ForwardKeyUp(ViewerKey key);
    void FocusViewer();
}
