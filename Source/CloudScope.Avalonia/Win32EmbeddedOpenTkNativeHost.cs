using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CloudScope.Platform.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace CloudScope.Avalonia;

public sealed unsafe class Win32EmbeddedOpenTkNativeHost : NativeControlHost
{
    private readonly HostController _hostController;
    private EmbeddedOpenTkViewerHost? _viewer;
    private IntPtr _hwnd;
    private DispatcherTimer? _pumpTimer;

    public Win32EmbeddedOpenTkNativeHost(HostController hostController)
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

    public string ExecuteViewerCommand(string commandText)
    {
        return _viewer?.ExecuteCommand(commandText) ?? "Embedded OpenTK host is not ready.";
    }

    public void ForwardKeyDown(ViewerKey key) => _viewer?.ForwardKeyDown(key);

    public void ForwardKeyUp(ViewerKey key) => _viewer?.SetKeyState(key, false);

    public void FocusViewer()
    {
        if (_hwnd != IntPtr.Zero)
            SetFocus(_hwnd);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            return base.CreateNativeControlCore(parent);

        _viewer = new EmbeddedOpenTkViewerHost(1280, 800, new OpenGlRenderBackend());
        _hwnd = GlfwGetWin32Window((IntPtr)_viewer.WindowPtr);
        ConfigureChildWindow(_hwnd, parent.Handle);
        _viewer.InitializeEmbedded();
        StartPump();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _pumpTimer?.Stop();
        _pumpTimer = null;
        _viewer?.Close();
        _viewer = null;
        _hwnd = IntPtr.Zero;
        base.DestroyNativeControlCore(control);
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, Math.Max(1, (int)finalRect.Width), Math.Max(1, (int)finalRect.Height), SwpNoZOrder | SwpNoActivate);
    }

    private static void ConfigureChildWindow(IntPtr child, IntPtr parent)
    {
        SetParent(child, parent);
        nint style = GetWindowLongPtr(child, GwlStyle);
        style &= ~(WsPopup | WsCaption | WsThickFrame);
        style |= WsChild | WsVisible;
        SetWindowLongPtr(child, GwlStyle, style);
        ShowWindow(child, SwShow);
        SetFocus(child);
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
            viewer.PumpEmbeddedFrame(1.0 / 60.0);
        };
        _pumpTimer.Start();
    }

    private const int GwlStyle = -16;
    private const nint WsChild = 0x40000000;
    private const nint WsVisible = 0x10000000;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int SwShow = 5;

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("glfw3", EntryPoint = "glfwGetWin32Window")]
    private static extern IntPtr GlfwGetWin32Window(IntPtr window);
}
