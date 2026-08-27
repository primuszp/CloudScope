using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CloudScope.Platform.MacOS.ObjC;

/// <summary>
/// Objective-C reference counting for handles the managed side owns. Every
/// <c>objc_release</c> in CloudScope goes through here, so there is one P/Invoke
/// declaration rather than one per renderer.
/// </summary>
[SupportedOSPlatform("macos")]
public static class ObjectiveCRuntime
{
    /// <summary>Releases the object, ignoring null handles.</summary>
    public static void Release(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            objc_release(handle);
    }

    /// <summary>Releases the object and clears the caller's handle.</summary>
    public static void Release(ref IntPtr handle)
    {
        Release(handle);
        handle = IntPtr.Zero;
    }

    [DllImport("libobjc.dylib", EntryPoint = "objc_release")]
    private static extern void objc_release(IntPtr handle);
}
