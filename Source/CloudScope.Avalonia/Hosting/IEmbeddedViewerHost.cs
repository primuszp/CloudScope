using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Loading;

namespace CloudScope.Avalonia.Hosting;

public interface IEmbeddedViewerHost
{
    string RendererName { get; }

    /// <summary>Live viewer state for the status bar and the properties panel.</summary>
    ViewerStatusSnapshot Status { get; }

    /// <summary>
    /// The viewer's command surface. Prompt, keywords, completion and execution all come
    /// from here, so no layer between the command line and the runtime restates them.
    /// </summary>
    ICommandExecutor Commands { get; }
    IReadOnlyCollection<LabelDefinition> LabelDefinitions { get; }
    string ActiveLabel { get; }
    int? ActiveInstanceId { get; }
    void LoadPointCloud(PointData[] points, float radius, Action? completed = null);
    void LoadPointCloud(PointCloudDataset dataset, Action? completed = null);
    void ResetViewer();

    void ForwardKeyDown(ViewerKey key);
    void ForwardKeyUp(ViewerKey key);
    void FocusViewer();
}
