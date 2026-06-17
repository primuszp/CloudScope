using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CloudScope.Platform.MacOS.ObjC;

[SupportedOSPlatform("macos")]
public static class ObjectiveCGeometryMessaging
{
    public static void InitializeWindow(
        IntPtr receiver,
        string selector,
        NSRect frame,
        ulong styleMask,
        ulong backingStore,
        bool defer)
    {
        ObjcMsgSendInitializeWindow(
            receiver,
            GetSelector(selector),
            frame,
            styleMask,
            backingStore,
            defer);
    }

    public static IntPtr InitializeView(IntPtr receiver, string selector, NSRect frame, IntPtr device) =>
        ObjcMsgSendInitializeView(receiver, GetSelector(selector), frame, device);

    private static IntPtr GetSelector(string name) => sel_registerName(name);

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendInitializeWindow(
        IntPtr receiver,
        IntPtr selector,
        NSRect frame,
        ulong styleMask,
        ulong backingStore,
        [MarshalAs(UnmanagedType.I1)] bool defer);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendInitializeView(
        IntPtr receiver,
        IntPtr selector,
        NSRect frame,
        IntPtr device);
}
