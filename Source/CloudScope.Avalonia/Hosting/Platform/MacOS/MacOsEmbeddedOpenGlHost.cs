using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CloudScope.Avalonia.Hosting;
using CloudScope.Platform.MacOS.ObjC;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CloudScope.Avalonia.Hosting.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed unsafe class MacOsEmbeddedOpenGlHost : EmbeddedOpenGlNativeHostBase
{
    private IntPtr _nsView;

    public MacOsEmbeddedOpenGlHost(HostController hostController) : base(hostController)
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
        NSViewInterop.SetFrameSize(_nsView, logicalWidth, logicalHeight);
        NSViewInterop.SetBounds(_nsView, new NSRect(0, 0, logicalWidth, logicalHeight));
        NSViewInterop.SetNeedsDisplay(_nsView);
    }

    private Vector2? GetMousePositionInNativeView()
    {
        if (Viewer == null || NSViewInterop.GetMouseLocation(_nsView) is not { } viewPoint)
            return null;

        return new Vector2((float)viewPoint.X, Viewer.ClientSize.Y - (float)viewPoint.Y);
    }
}
