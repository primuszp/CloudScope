using CloudScope.Commands;

namespace CloudScope.Avalonia.Hosting;

public interface IEmbeddedOpenTkNativeHost
{
    string CommandPrompt { get; }
    void LoadPointCloud(PointData[] points, float radius);
    void ResetViewer();

    bool IsKnownCommand(string name);
    CommandResult ExecuteViewerCommandResult(string commandText);
    string ExecuteViewerCommand(string commandText);

    void ForwardKeyDown(ViewerKey key);
    void ForwardKeyUp(ViewerKey key);
    void FocusViewer();
}
