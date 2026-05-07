using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    internal class MTKViewDelegate : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DrawDelegate(IntPtr id, IntPtr cmd, IntPtr view);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SizeChangeDelegate(IntPtr id, IntPtr cmd, IntPtr view, NSRect rect);

        // Keep delegates alive to prevent GC collection
        private readonly DrawDelegate _draw;
        private readonly SizeChangeDelegate _sizeChange;

        public Action<MTKView>? OnDraw;
        public Action<MTKView, NSRect>? OnSizeChange;

        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTKViewDelegate d) => d.NativePtr;

        public unsafe MTKViewDelegate()
        {
            _draw = (_, _, view) => OnDraw?.Invoke(new MTKView(view));
            _sizeChange = (_, _, view, rect) => OnSizeChange?.Invoke(new MTKView(view), rect);

            var name = Utf8StringMarshaller.ConvertToUnmanaged("CloudScopeMTKViewDelegate");
            var t1 = Utf8StringMarshaller.ConvertToUnmanaged("v@:@");
            var t2 = Utf8StringMarshaller.ConvertToUnmanaged("v@:@{CGRect={CGPoint=dd}{CGPoint=dd}}");

            var cls = ObjectiveC.objc_allocateClassPair(new ObjectiveCClass("NSObject"), (char*)name, 0);
            ObjectiveC.class_addMethod(cls, "drawInMTKView:", Marshal.GetFunctionPointerForDelegate(_draw), (char*)t1);
            ObjectiveC.class_addMethod(cls, "mtkView:drawableSizeWillChange:", Marshal.GetFunctionPointerForDelegate(_sizeChange), (char*)t2);
            ObjectiveC.objc_registerClassPair(cls);

            NativePtr = new ObjectiveCClass(cls).AllocInit();
        }

        public void Dispose() => NativePtr = IntPtr.Zero;
    }
}
