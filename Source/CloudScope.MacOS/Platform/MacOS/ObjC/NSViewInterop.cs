using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CloudScope.Platform.MacOS.ObjC;

/// <summary>
/// The handful of AppKit messages the embedded hosts need to size an <c>NSView</c> and
/// locate the cursor inside it. Every host goes through this type so the selector and
/// <c>objc_msgSend</c> declarations exist once.
/// </summary>
[SupportedOSPlatform("macos")]
public static class NSViewInterop
{
    /// <summary>Sets the view's frame size in logical (point) units.</summary>
    public static void SetFrameSize(IntPtr view, double width, double height)
    {
        if (view != IntPtr.Zero)
            SendSize(view, SelSetFrameSize, new NSSize(width, height));
    }

    /// <summary>Sets the view's bounds rectangle in logical (point) units.</summary>
    public static void SetBounds(IntPtr view, NSRect bounds)
    {
        if (view != IntPtr.Zero)
            SendRect(view, SelSetBounds, bounds);
    }

    public static void SetNeedsDisplay(IntPtr view, bool needsDisplay = true)
    {
        if (view != IntPtr.Zero)
            SendBool(view, SelSetNeedsDisplay, needsDisplay);
    }

    /// <summary>The window owning <paramref name="view"/>, or <see cref="IntPtr.Zero"/> when unparented.</summary>
    public static IntPtr GetWindow(IntPtr view) =>
        view == IntPtr.Zero ? IntPtr.Zero : SendIntPtr(view, SelWindow);

    /// <summary>
    /// The current cursor position in the view's coordinate space (AppKit's bottom-left
    /// origin), or <c>null</c> when the view is not on screen.
    /// </summary>
    public static NSPoint? GetMouseLocation(IntPtr view)
    {
        IntPtr window = GetWindow(view);
        if (window == IntPtr.Zero)
            return null;

        NSPoint screenPoint = SendPoint(NSEventClass, SelMouseLocation);
        NSPoint windowPoint = SendPointArg(window, SelConvertPointFromScreen, screenPoint);
        return SendPointFromView(view, SelConvertPointFromView, windowPoint, IntPtr.Zero);
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
    private static extern void SendRect(IntPtr receiver, IntPtr selector, NSRect rect);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendSize(IntPtr receiver, IntPtr selector, NSSize size);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSPoint SendPoint(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSPoint SendPointArg(IntPtr receiver, IntPtr selector, NSPoint point);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSPoint SendPointFromView(IntPtr receiver, IntPtr selector, NSPoint point, IntPtr fromView);
}
