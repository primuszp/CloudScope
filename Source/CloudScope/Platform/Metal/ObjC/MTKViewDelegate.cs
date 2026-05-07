using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    internal sealed class MTKViewDelegate : IDisposable
    {
        // ── Static callbacks — stored as static fields so the GC never collects them ──
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DrawDelegate(IntPtr id, IntPtr cmd, IntPtr view);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SizeChangeDelegate(IntPtr id, IntPtr cmd, IntPtr view, NSSize size);

        private static readonly DrawDelegate      s_draw       = OnDraw;
        private static readonly SizeChangeDelegate s_sizeChange = OnSizeChange;
        private static readonly Lazy<IntPtr>      s_class      = new(RegisterClass);

        // Maps ObjC instance pointer → C# wrapper so static callbacks can find the right instance.
        private static readonly ConcurrentDictionary<IntPtr, MTKViewDelegate> s_instances = new();

        // ── Per-instance ─────────────────────────────────────────────────────────
        public Action<MTKView>?           OnDraw_;
        public Action<MTKView, NSSize>?   OnSizeChange_;
        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTKViewDelegate d) => d.NativePtr;

        private bool _disposed;

        public MTKViewDelegate()
        {
            NativePtr = new ObjectiveCClass(s_class.Value).AllocInit();
            if (NativePtr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to allocate MTKViewDelegate ObjC instance.");
            s_instances[NativePtr] = this;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_instances.TryRemove(NativePtr, out _);
            NativePtr = IntPtr.Zero;
        }

        // ── Static ObjC callbacks ─────────────────────────────────────────────────

        private static void OnDraw(IntPtr id, IntPtr cmd, IntPtr view)
        {
            if (s_instances.TryGetValue(id, out var inst) && !inst._disposed)
                inst.OnDraw_?.Invoke(new MTKView(view));
        }

        private static void OnSizeChange(IntPtr id, IntPtr cmd, IntPtr view, NSSize size)
        {
            if (s_instances.TryGetValue(id, out var inst) && !inst._disposed)
                inst.OnSizeChange_?.Invoke(new MTKView(view), size);
        }

        // ── ObjC class registration (runs once, lazily) ───────────────────────────

        private static unsafe IntPtr RegisterClass()
        {
            var name       = NullTermUtf8("CloudScopeMTKViewDelegate");
            var drawTypes  = NullTermUtf8("v@:@");
            var sizeTypes  = NullTermUtf8("v@:@{CGSize=dd}");

            fixed (byte* namePtr      = name)
            fixed (byte* drawTypesPtr = drawTypes)
            fixed (byte* sizeTypesPtr = sizeTypes)
            {
                var cls = ObjectiveC.objc_allocateClassPair(
                    new ObjectiveCClass("NSObject"), (char*)namePtr, 0);
                if (cls == IntPtr.Zero)
                    throw new InvalidOperationException("objc_allocateClassPair failed for MTKViewDelegate.");

                ObjectiveC.class_addMethod(cls, "drawInMTKView:",
                    Marshal.GetFunctionPointerForDelegate(s_draw), (char*)drawTypesPtr);
                ObjectiveC.class_addMethod(cls, "mtkView:drawableSizeWillChange:",
                    Marshal.GetFunctionPointerForDelegate(s_sizeChange), (char*)sizeTypesPtr);

                ObjectiveC.objc_registerClassPair(cls);
                return cls;
            }
        }

        private static byte[] NullTermUtf8(string s) => Encoding.UTF8.GetBytes(s + '\0');
    }
}
