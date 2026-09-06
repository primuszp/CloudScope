using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using CloudScope.Avalonia.Hosting;

namespace CloudScope.Avalonia.Hosting.Platform.Windows;

public sealed unsafe class Win32EmbeddedOpenGlHost : EmbeddedOpenGlNativeHostBase
{
    private IntPtr _hwnd;

    public Win32EmbeddedOpenGlHost(HostController hostController) : base(hostController)
    {
    }

    public override void FocusViewer()
    {
        if (_hwnd != IntPtr.Zero)
            SetFocus(_hwnd);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            return base.CreateNativeControlCore(parent);

        EmbeddedOpenTkViewerHost viewer = CreateViewer();
        _hwnd = GlfwGetWin32Window((IntPtr)viewer.WindowPtr);
        ConfigureChildWindow(_hwnd, parent.Handle);
        InitializeViewerAndStartPump();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DestroyViewer();
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
        style &= ~(WsPopup | WsCaption | WsBorder | WsDlgFrame | WsThickFrame);
        style |= WsChild | WsVisible;
        SetWindowLongPtr(child, GwlStyle, style);

        // GLFW's window keeps WS_EX_* frame edges that paint a 1px line in the system accent
        // colour along the viewport border. Clear them too.
        nint exStyle = GetWindowLongPtr(child, GwlExStyle);
        exStyle &= ~(WsExClientEdge | WsExStaticEdge | WsExWindowEdge | WsExDlgModalFrame);
        SetWindowLongPtr(child, GwlExStyle, exStyle);

        // Style edits only repaint the non-client frame after a frame-changed SetWindowPos;
        // without this the child keeps the frame line it had as a top-level window.
        SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        ShowWindow(child, SwShow);
        SetFocus(child);
    }

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const nint WsChild = 0x40000000;
    private const nint WsVisible = 0x10000000;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const nint WsCaption = 0x00C00000;
    private const nint WsBorder = 0x00800000;
    private const nint WsDlgFrame = 0x00400000;
    private const nint WsThickFrame = 0x00040000;
    private const nint WsExDlgModalFrame = 0x00000001;
    private const nint WsExWindowEdge = 0x00000100;
    private const nint WsExClientEdge = 0x00000200;
    private const nint WsExStaticEdge = 0x00020000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
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
