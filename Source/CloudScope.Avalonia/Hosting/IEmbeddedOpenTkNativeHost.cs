namespace CloudScope.Avalonia.Hosting;

public interface IEmbeddedOpenTkNativeHost
{
    void LoadPointCloud(PointData[] points, float radius);

    string ExecuteViewerCommand(string commandText);

    void ForwardKeyDown(ViewerKey key);

    void ForwardKeyUp(ViewerKey key);

    void FocusViewer();
}
