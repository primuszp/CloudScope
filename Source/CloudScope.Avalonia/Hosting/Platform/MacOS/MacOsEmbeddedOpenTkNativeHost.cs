using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CloudScope.Avalonia.Hosting;
using CloudScope.Platform.MacOS.ObjC;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CloudScope.Avalonia.Hosting.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed unsafe class MacOsEmbeddedOpenTkNativeHost : EmbeddedOpenTkNativeHostBase
{
    private IntPtr _nsView;

    public MacOsEmbeddedOpenTkNativeHost(HostController hostController) : base(hostController)
    {
    }

    public override void FocusViewer()
    {
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
            return base.CreateNativeControlCore(parent);

        EmbeddedOpenTkViewerHost viewer = CreateViewer(GetMousePositionInNativeView);
        _nsView = GLFW.GetCocoaView(viewer.WindowPtr);
        if (_nsView == IntPtr.Zero)
            throw new InvalidOperationException("GLFW did not return an NSView for the embedded OpenTK window.");

        InitializeViewerAndStartPump(SyncFramebufferViewport);
        return new PlatformHandle(_nsView, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DestroyViewer();
        _nsView = IntPtr.Zero;

        base.DestroyNativeControlCore(control);
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (Viewer != null)
        {
            int logicalWidth = Math.Max(1, (int)Math.Round(finalRect.Width));
            int logicalHeight = Math.Max(1, (int)Math.Round(finalRect.Height));
            SyncViewerSize(Viewer, logicalWidth, logicalHeight);
        }
    }

    private void SyncViewerSize(EmbeddedOpenTkViewerHost viewer, int logicalWidth, int logicalHeight)
    {
        SetNativeViewFrame(logicalWidth, logicalHeight);
        viewer.ClientSize = new Vector2i(logicalWidth, logicalHeight);
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

        var size = new NSSize(logicalWidth, logicalHeight);
        var bounds = new NSRect(0, 0, logicalWidth, logicalHeight);
        ObjcMsgSendSize(_nsView, SelSetFrameSize, size);
        ObjcMsgSendRect(_nsView, SelSetBounds, bounds);
        ObjcMsgSendBool(_nsView, SelSetNeedsDisplay, true);
    }

    private Vector2? GetMousePositionInNativeView()
    {
        if (_nsView == IntPtr.Zero || Viewer == null)
            return null;

        IntPtr window = ObjcMsgSendIntPtr(_nsView, SelWindow);
        if (window == IntPtr.Zero)
            return null;

        NSPoint screenPoint = ObjcMsgSendPoint(NSEventClass, SelMouseLocation);
        NSPoint windowPoint = ObjcMsgSendPointArg(window, SelConvertPointFromScreen, screenPoint);
        NSPoint viewPoint = ObjcMsgSendPointArgIntPtr(_nsView, SelConvertPointFromView, windowPoint, IntPtr.Zero);

        float x = (float)viewPoint.X;
        float y = Viewer.ClientSize.Y - (float)viewPoint.Y;
        return new Vector2(x, y);
    }

    private static readonly IntPtr SelSetFrameSize = sel_registerName("setFrameSize:");
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
    private static extern void ObjcMsgSendSize(IntPtr receiver, IntPtr selector, NSSize size);

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
}
