using System.Runtime.Versioning;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    internal class NSApplication
    {
        public IntPtr NativePtr;

        public NSApplication()
            => NativePtr = ObjectiveC.IntPtr_objc_msgSend(new ObjectiveCClass("NSApplication"), "sharedApplication");

        public NSApplication(IntPtr ptr) => NativePtr = ptr;

        public void Run() => ObjectiveC.objc_msgSend(NativePtr, "run");
        public void Stop() => ObjectiveC.objc_msgSend(NativePtr, "stop:", IntPtr.Zero);
        public void ActivateIgnoringOtherApps(bool flag) => ObjectiveC.objc_msgSend(NativePtr, "activateIgnoringOtherApps:", flag);
        public void SetActivationPolicy(long policy) => ObjectiveC.objc_msgSend(NativePtr, "setActivationPolicy:", policy);
        public void SetDelegate(NSApplicationDelegate d) => ObjectiveC.objc_msgSend(NativePtr, "setDelegate:", d.NativePtr);
    }
}
