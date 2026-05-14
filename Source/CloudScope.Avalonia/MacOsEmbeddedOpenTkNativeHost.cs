using System.Runtime.Versioning;
using System.Runtime.InteropServices;
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
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
            return base.CreateNativeControlCore(parent);

        _viewer = new EmbeddedOpenTkViewerHost(1280, 800, new OpenGlRenderBackend())
        {
            LogicalMousePositionProvider = GetMousePositionInNativeView
        };
        _nsView = GLFW.GetCocoaView(_viewer.WindowPtr);
        if (_nsView == IntPtr.Zero)
            throw new InvalidOperationException("GLFW did not return an NSView for the embedded OpenTK window.");

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

        _nsView = IntPtr.Zero;

        base.DestroyNativeControlCore(control);
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_viewer != null)
        {
            int logicalWidth = Math.Max(1, (int)Math.Round(finalRect.Width));
            int logicalHeight = Math.Max(1, (int)Math.Round(finalRect.Height));
            SyncViewerSize(_viewer, logicalWidth, logicalHeight);
        }
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
            SyncFramebufferViewport(viewer);
            viewer.PumpEmbeddedFrame(1.0 / 60.0);
        };
        _pumpTimer.Start();
    }

    private void SyncViewerSize(EmbeddedOpenTkViewerHost viewer, int logicalWidth, int logicalHeight)
    {
        SetNativeViewFrame(logicalWidth, logicalHeight);
        viewer.ClientSize = new Vector2i(logicalWidth, logicalHeight);
        GLFW.SetWindowSize(viewer.WindowPtr, logicalWidth, logicalHeight);
        var (pixelWidth, pixelHeight) = ToBackingPixelSize(logicalWidth, logicalHeight);
        viewer.SyncFramebufferViewport(pixelWidth, pixelHeight);
    }

    private void SyncFramebufferViewport(EmbeddedOpenTkViewerHost viewer)
    {
        var (pixelWidth, pixelHeight) = ToBackingPixelSize(viewer.ClientSize.X, viewer.ClientSize.Y);
        viewer.SyncFramebufferViewport(pixelWidth, pixelHeight);
    }

    private (int width, int height) ToBackingPixelSize(int logicalWidth, int logicalHeight)
    {
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)Math.Round(logicalWidth * scale));
        int height = Math.Max(1, (int)Math.Round(logicalHeight * scale));
        return (width, height);
    }

    private void SetNativeViewFrame(int logicalWidth, int logicalHeight)
    {
        if (_nsView == IntPtr.Zero)
            return;

        var rect = new NSRect(0, 0, logicalWidth, logicalHeight);
        ObjcMsgSendRect(_nsView, SelSetFrame, rect);
        ObjcMsgSendRect(_nsView, SelSetBounds, rect);
        ObjcMsgSendBool(_nsView, SelSetNeedsDisplay, true);
    }

    private Vector2? GetMousePositionInNativeView()
    {
        if (_nsView == IntPtr.Zero || _viewer == null)
            return null;

        IntPtr window = ObjcMsgSendIntPtr(_nsView, SelWindow);
        if (window == IntPtr.Zero)
            return null;

        NSPoint screenPoint = ObjcMsgSendPoint(NSEventClass, SelMouseLocation);
        NSPoint windowPoint = ObjcMsgSendPointArg(window, SelConvertPointFromScreen, screenPoint);
        NSPoint viewPoint = ObjcMsgSendPointArgIntPtr(_nsView, SelConvertPointFromView, windowPoint, IntPtr.Zero);

        float x = (float)viewPoint.X;
        float y = _viewer.ClientSize.Y - (float)viewPoint.Y;
        return new Vector2(x, y);
    }

    private static readonly IntPtr SelSetFrame = sel_registerName("setFrame:");
    private static readonly IntPtr SelSetBounds = sel_registerName("setBounds:");
    private static readonly IntPtr SelSetNeedsDisplay = sel_registerName("setNeedsDisplay:");
    private static readonly IntPtr SelWindow = sel_registerName("window");
    private static readonly IntPtr SelMouseLocation = sel_registerName("mouseLocation");
    private static readonly IntPtr SelConvertPointFromScreen = sel_registerName("convertPointFromScreen:");
    private static readonly IntPtr SelConvertPointFromView = sel_registerName("convertPoint:fromView:");
    private static readonly IntPtr NSEventClass = objc_getClass("NSEvent");

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendRect(IntPtr receiver, IntPtr selector, NSRect rect);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendBool(IntPtr receiver, IntPtr selector, bool value);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSPoint ObjcMsgSendPoint(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSPoint ObjcMsgSendPointArg(IntPtr receiver, IntPtr selector, NSPoint point);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSPoint ObjcMsgSendPointArgIntPtr(IntPtr receiver, IntPtr selector, NSPoint point, IntPtr view);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NSPoint
    {
        public readonly double X;
        public readonly double Y;

        public NSPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NSRect
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Width;
        public readonly double Height;

        public NSRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
