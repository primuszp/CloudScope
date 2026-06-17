using Avalonia.Controls;
using Avalonia.Threading;
using CloudScope.Commands;
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
    public string CommandPrompt => Viewer?.CommandPrompt ?? "Command:";

    public void LoadPointCloud(PointData[] points, float radius)
    {
        EmbeddedOpenTkViewerHost? viewer = Viewer;
        if (viewer == null)
            return;

        viewer.Enqueue(v => v.LoadPointCloud(points, radius));
    }

    public void ResetViewer()
    {
        EmbeddedOpenTkViewerHost? viewer = Viewer;
        if (viewer == null)
            return;

        viewer.Enqueue(v => v.Reset());
    }

    public CommandResult ExecuteViewerCommandResult(string commandText) =>
        Viewer?.ExecuteCommandResult(commandText) ?? CommandResult.End("Embedded OpenTK host is not ready.");

    public bool IsKnownCommand(string name) => Viewer?.IsKnownCommand(name) == true;

    public string ExecuteViewerCommand(string commandText) =>
        ExecuteViewerCommandResult(commandText).Message;

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

        // Deterministic GL teardown on the UI thread while the context is current.
        // Leaving it to the GameWindow finalizer destroys the native GLFW window on
        // the GC thread → access violation (0xC0000005) at a nondeterministic time.
        Viewer?.ShutdownEmbedded();
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
