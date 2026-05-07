using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    internal class NSWindow
    {
        public IntPtr NativePtr;

        public NSWindow(NSRect rect, ulong styleMask)
        {
            NativePtr = new ObjectiveCClass("NSWindow").Alloc();
            ObjectiveC.objc_msgSend(NativePtr, "initWithContentRect:styleMask:backing:defer:",
                rect, styleMask, 2UL, false);
        }

        public NSString Title
        {
            get => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "title"));
            set => ObjectiveC.objc_msgSend(NativePtr, "setTitle:", value);
        }

        public void SetContentView(IntPtr view)
            => ObjectiveC.objc_msgSend(NativePtr, "setContentView:", view);

        public void MakeKeyAndOrderFront()
            => ObjectiveC.objc_msgSend(NativePtr, "makeKeyAndOrderFront:", IntPtr.Zero);
    }

    [Flags]
    internal enum NSStyleMask : ulong
    {
        Titled = 1 << 0,
        Closable = 1 << 1,
        Miniaturizable = 1 << 2,
        Resizable = 1 << 3,
    }
}
