using Avalonia.Controls;
using Avalonia.Threading;
using CloudScope.Platform.OpenGL;
using OpenTK.Mathematics;

namespace CloudScope.Avalonia.Hosting;

public abstract class EmbeddedOpenTkNativeHostBase : NativeControlHost, IEmbeddedOpenTkNativeHost
{
    private DispatcherTimer? _pumpTimer;

    protected EmbeddedOpenTkNativeHostBase(HostController hostController)
    {
        hostController.SetEmbeddedHost(this);
    }

    protected EmbeddedOpenTkViewerHost? Viewer { get; private set; }

    public void LoadPointCloud(PointData[] points, float radius)
    {
        EmbeddedOpenTkViewerHost? viewer = Viewer;
        if (viewer == null)
            return;

        viewer.Enqueue(v => v.LoadPointCloud(points, radius));
    }

    public string ExecuteViewerCommand(string commandText) =>
        Viewer?.ExecuteCommand(commandText) ?? "Embedded OpenTK host is not ready.";

    public void ForwardKeyDown(ViewerKey key) => Viewer?.ForwardKeyDown(key);

    public void ForwardKeyUp(ViewerKey key) => Viewer?.SetKeyState(key, false);

    public abstract void FocusViewer();

    protected EmbeddedOpenTkViewerHost CreateViewer(Func<Vector2?>? logicalMousePositionProvider = null)
    {
        Viewer = new EmbeddedOpenTkViewerHost(1280, 800, new OpenGlRenderBackend())
        {
            LogicalMousePositionProvider = logicalMousePositionProvider
        };

        return Viewer;
    }

    protected void InitializeViewerAndStartPump(Action<EmbeddedOpenTkViewerHost>? beforeFrame = null)
    {
        EmbeddedOpenTkViewerHost viewer = Viewer
            ?? throw new InvalidOperationException("Embedded OpenTK viewer has not been created.");

        viewer.InitializeEmbedded();
        StartPump(beforeFrame);
    }

    protected void DestroyViewer()
    {
        _pumpTimer?.Stop();
        _pumpTimer = null;

        Viewer?.Close();
        Viewer = null;
    }

    private void StartPump(Action<EmbeddedOpenTkViewerHost>? beforeFrame)
    {
        _pumpTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _pumpTimer.Tick += (_, _) =>
        {
            EmbeddedOpenTkViewerHost? viewer = Viewer;
            if (viewer == null)
                return;

            viewer.PumpActions();
            beforeFrame?.Invoke(viewer);
            viewer.PumpEmbeddedFrame(1.0 / 60.0);
        };
        _pumpTimer.Start();
    }
}
