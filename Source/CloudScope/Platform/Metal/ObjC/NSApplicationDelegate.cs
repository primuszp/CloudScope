using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    internal class NSApplicationDelegate
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NotifDelegate(IntPtr id, IntPtr cmd, IntPtr notif);

        private readonly NotifDelegate _willFinish;
        private readonly NotifDelegate _didFinish;

        public Action<NSNotification>? OnWillFinishLaunching;
        public Action<NSNotification>? OnDidFinishLaunching;

        public IntPtr NativePtr;

        public unsafe NSApplicationDelegate()
        {
            _willFinish = (_, _, n) => OnWillFinishLaunching?.Invoke(new NSNotification(n));
            _didFinish = (_, _, n) => OnDidFinishLaunching?.Invoke(new NSNotification(n));

            var name = Utf8StringMarshaller.ConvertToUnmanaged("CloudScopeAppDelegate");
            var types = Utf8StringMarshaller.ConvertToUnmanaged("v@:@");

            var cls = ObjectiveC.objc_allocateClassPair(new ObjectiveCClass("NSObject"), (char*)name, 0);
            ObjectiveC.class_addMethod(cls, "applicationWillFinishLaunching:", Marshal.GetFunctionPointerForDelegate(_willFinish), (char*)types);
            ObjectiveC.class_addMethod(cls, "applicationDidFinishLaunching:", Marshal.GetFunctionPointerForDelegate(_didFinish), (char*)types);
            ObjectiveC.objc_registerClassPair(cls);

            NativePtr = new ObjectiveCClass(cls).AllocInit();
        }
    }
}
