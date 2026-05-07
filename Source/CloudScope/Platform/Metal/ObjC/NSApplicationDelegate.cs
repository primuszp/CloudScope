using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    internal sealed class NSApplicationDelegate
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NotifDelegate(IntPtr id, IntPtr cmd, IntPtr notif);

        // Static callbacks — GC never collects these.
        private static readonly NotifDelegate s_willFinish = OnWillFinish;
        private static readonly NotifDelegate s_didFinish  = OnDidFinish;
        private static readonly Lazy<IntPtr>  s_class      = new(RegisterClass);

        private static readonly ConcurrentDictionary<IntPtr, NSApplicationDelegate> s_instances = new();

        public Action<NSNotification>? OnWillFinishLaunching;
        public Action<NSNotification>? OnDidFinishLaunching;

        public IntPtr NativePtr;

        public NSApplicationDelegate()
        {
            NativePtr = new ObjectiveCClass(s_class.Value).AllocInit();
            if (NativePtr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to allocate NSApplicationDelegate ObjC instance.");
            s_instances[NativePtr] = this;
        }

        private static void OnWillFinish(IntPtr id, IntPtr cmd, IntPtr notif)
        {
            if (s_instances.TryGetValue(id, out var inst))
                inst.OnWillFinishLaunching?.Invoke(new NSNotification(notif));
        }

        private static void OnDidFinish(IntPtr id, IntPtr cmd, IntPtr notif)
        {
            if (s_instances.TryGetValue(id, out var inst))
                inst.OnDidFinishLaunching?.Invoke(new NSNotification(notif));
        }

        private static unsafe IntPtr RegisterClass()
        {
            var name  = NullTermUtf8("CloudScopeAppDelegate");
            var types = NullTermUtf8("v@:@");

            fixed (byte* namePtr  = name)
            fixed (byte* typesPtr = types)
            {
                var cls = ObjectiveC.objc_allocateClassPair(
                    new ObjectiveCClass("NSObject"), (char*)namePtr, 0);
                if (cls == IntPtr.Zero)
                    throw new InvalidOperationException("objc_allocateClassPair failed for NSApplicationDelegate.");

                ObjectiveC.class_addMethod(cls, "applicationWillFinishLaunching:",
                    Marshal.GetFunctionPointerForDelegate(s_willFinish), (char*)typesPtr);
                ObjectiveC.class_addMethod(cls, "applicationDidFinishLaunching:",
                    Marshal.GetFunctionPointerForDelegate(s_didFinish), (char*)typesPtr);

                ObjectiveC.objc_registerClassPair(cls);
                return cls;
            }
        }

        private static byte[] NullTermUtf8(string s) => Encoding.UTF8.GetBytes(s + '\0');
    }
}
