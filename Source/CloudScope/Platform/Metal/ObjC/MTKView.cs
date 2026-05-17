using System.Runtime.Versioning;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;
using NSRect = CloudScope.Platform.MacOS.ObjC.NSRect;
using ObjectiveCGeometryMessaging = CloudScope.Platform.MacOS.ObjC.ObjectiveCGeometryMessaging;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    internal class MTKView
    {
        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTKView v) => v.NativePtr;

        public MTKView(IntPtr ptr) => NativePtr = ptr;

        public MTKView(NSRect frame, MTLDevice device)
        {
            var ptr = new ObjectiveCClass("MTKView").Alloc();
            NativePtr = ObjectiveCGeometryMessaging.InitializeView(
                ptr,
                "initWithFrame:device:",
                frame,
                device.NativePtr);
        }

        public MTLPixelFormat ColorPixelFormat
        {
            set => ObjectiveC.objc_msgSend(NativePtr, new Selector("setColorPixelFormat:"), value);
        }

        public MTLPixelFormat DepthStencilPixelFormat
        {
            set => ObjectiveC.objc_msgSend(NativePtr, new Selector("setDepthStencilPixelFormat:"), value);
        }

        public MTLClearColor ClearColor
        {
            set => ObjectiveC.objc_msgSend(NativePtr, new Selector("setClearColor:"), value);
        }

        public MTKViewDelegate? Delegate
        {
            set => ObjectiveC.objc_msgSend(NativePtr, "setDelegate:", value?.NativePtr ?? IntPtr.Zero);
        }

        public bool FramebufferOnly
        {
            set => ObjectiveC.objc_msgSend(NativePtr, "setFramebufferOnly:", value);
        }

        public bool Paused
        {
            set => ObjectiveC.objc_msgSend(NativePtr, "setPaused:", value);
        }

        public bool EnableSetNeedsDisplay
        {
            set => ObjectiveC.objc_msgSend(NativePtr, "setEnableSetNeedsDisplay:", value);
        }

        public MTLDevice Device
            => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "device"));

        public CAMetalDrawable CurrentDrawable
            => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "currentDrawable"));

        public MTLRenderPassDescriptor CurrentRenderPassDescriptor
            => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "currentRenderPassDescriptor"));
    }
}
