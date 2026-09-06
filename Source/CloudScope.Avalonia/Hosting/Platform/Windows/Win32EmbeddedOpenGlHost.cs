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
        // Strip every frame style BEFORE reparenting (MSDN: set WS_CHILD before SetParent).
        // GLFW makes an undecorated window WS_POPUP | WS_THICKFRAME (so it can still be edge
        // resized), and WS_THICKFRAME alone is enough for a visible sizing border.
        nint style = GetWindowLongPtr(child, GwlStyle);
        style &= ~(WsPopup | WsCaption | WsBorder | WsDlgFrame | WsThickFrame |
                   WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        style |= WsChild | WsVisible;
        SetWindowLongPtr(child, GwlStyle, style);

        nint exStyle = GetWindowLongPtr(child, GwlExStyle);
        exStyle &= ~(WsExClientEdge | WsExStaticEdge | WsExWindowEdge | WsExDlgModalFrame | WsExAppWindow);
        SetWindowLongPtr(child, GwlExStyle, exStyle);

        SetParent(child, parent);

        // Windows 11 draws a 1px border around a window in the system accent colour and rounds
        // its corners, and keeps doing so for a former top-level window that is now a child.
        // Tell DWM: no border colour, square corners, no non-client rendering at all. Every
        // call is a harmless no-op on Windows 10 and earlier.
        int colorNone = unchecked((int)0xFFFFFFFE);       // DWMWA_COLOR_NONE
        TryDwmSetInt(child, DwmwaBorderColor, colorNone);
        TryDwmSetInt(child, DwmwaWindowCornerPreference, DwmwcpDoNotRound);
        TryDwmSetInt(child, DwmwaNcRenderingPolicy, DwmncrpDisabled);

        // Style edits only repaint the non-client frame after a frame-changed SetWindowPos.
        SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        ShowWindow(child, SwShow);
        SetFocus(child);
    }

    private static void TryDwmSetInt(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            int local = value;
            DwmSetWindowAttribute(hwnd, attribute, ref local, sizeof(int));
        }
        catch
        {
            // dwmapi missing, or the attribute is newer than this Windows build.
        }
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
    private const nint WsSysMenu = 0x00080000;
    private const nint WsMinimizeBox = 0x00020000;
    private const nint WsMaximizeBox = 0x00010000;
    private const nint WsExDlgModalFrame = 0x00000001;
    private const nint WsExWindowEdge = 0x00000100;
    private const nint WsExClientEdge = 0x00000200;
    private const nint WsExStaticEdge = 0x00020000;
    private const nint WsExAppWindow = 0x00040000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int SwShow = 5;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmncrpDisabled = 1;
    private const int DwmwcpDoNotRound = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
