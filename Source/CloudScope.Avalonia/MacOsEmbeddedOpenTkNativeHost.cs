using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CloudScope.Platform.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CloudScope.Avalonia;

[SupportedOSPlatform("macos")]
public sealed unsafe class MacOsEmbeddedOpenTkNativeHost : NativeControlHost, IEmbeddedOpenTkNativeHost
{
    private readonly HostController _hostController;
    private EmbeddedOpenTkViewerHost? _viewer;
    private IntPtr _nsView;
    private DispatcherTimer? _pumpTimer;

    public MacOsEmbeddedOpenTkNativeHost(HostController hostController)
    {
        _hostController = hostController;
        _hostController.SetEmbeddedHost(this);
    }

    public void LoadPointCloud(PointData[] points, float radius)
    {
        EmbeddedOpenTkViewerHost? viewer = _viewer;
        if (viewer == null)
            return;

        viewer.Enqueue(v => v.LoadPointCloud(points, radius));
    }

    public string ExecuteViewerCommand(string commandText) =>
        _viewer?.ExecuteCommand(commandText) ?? "Embedded OpenTK host is not ready.";

    public void ForwardKeyDown(ViewerKey key) => _viewer?.ForwardKeyDown(key);

    public void ForwardKeyUp(ViewerKey key) => _viewer?.SetKeyState(key, false);

    public void FocusViewer()
    {
        if (_nsView == IntPtr.Zero)
            return;

        IntPtr window = IntPtr_objc_msgSend(_nsView, sel_window);
        if (window != IntPtr.Zero)
            Byte_objc_msgSend_IntPtr(window, sel_makeFirstResponder, _nsView);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
            return base.CreateNativeControlCore(parent);

        _viewer = new EmbeddedOpenTkViewerHost(1280, 800, new OpenGlRenderBackend());
        _nsView = GLFW.GetCocoaView(_viewer.WindowPtr);
        if (_nsView == IntPtr.Zero)
            throw new InvalidOperationException("GLFW did not return an NSView for the embedded OpenTK window.");

        Retain(_nsView);
        ConfigureView(_nsView);
        _viewer.InitializeEmbedded();
        StartPump();
        return new PlatformHandle(_nsView, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _pumpTimer?.Stop();
        _pumpTimer = null;
        _viewer?.Close();
        _viewer = null;

        if (_nsView != IntPtr.Zero)
        {
            Release(_nsView);
            _nsView = IntPtr.Zero;
        }

        base.DestroyNativeControlCore(control);
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_viewer != null)
        {
            _viewer.ClientSize = new Vector2i(Math.Max(1, (int)finalRect.Width), Math.Max(1, (int)finalRect.Height));
            _viewer.SyncFramebufferViewport();
        }
    }

    private static void ConfigureView(IntPtr nsView)
    {
        Void_objc_msgSend_byte(nsView, sel_setWantsLayer, 1);
    }

    private void StartPump()
    {
        _pumpTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _pumpTimer.Tick += (_, _) =>
        {
            EmbeddedOpenTkViewerHost? viewer = _viewer;
            if (viewer == null)
                return;

            viewer.PumpActions();
            viewer.SyncFramebufferViewport();
            viewer.PumpEmbeddedFrame(1.0 / 60.0);
        };
        _pumpTimer.Start();
    }

    private static void Retain(IntPtr obj) => IntPtr_objc_msgSend(obj, sel_retain);

    private static void Release(IntPtr obj) => IntPtr_objc_msgSend(obj, sel_release);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_byte(IntPtr receiver, IntPtr selector, byte value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte Byte_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    private static readonly IntPtr sel_retain = SelRegisterName("retain");
    private static readonly IntPtr sel_release = SelRegisterName("release");
    private static readonly IntPtr sel_window = SelRegisterName("window");
    private static readonly IntPtr sel_makeFirstResponder = SelRegisterName("makeFirstResponder:");
    private static readonly IntPtr sel_setWantsLayer = SelRegisterName("setWantsLayer:");
}
