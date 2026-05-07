using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace CloudScope.Platform.Metal.ObjC
{
    /// <summary>
    /// MTKView subclass registered via ObjC runtime that forwards
    /// mouse and keyboard events to the ViewerController.
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal sealed class MTKEventView : IDisposable
    {
        // ── ObjC stubs ────────────────────────────────────────────────────────────
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MouseDelegate(IntPtr self, IntPtr cmd, IntPtr evt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ScrollDelegate(IntPtr self, IntPtr cmd, IntPtr evt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void KeyDelegate(IntPtr self, IntPtr cmd, IntPtr evt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool BoolDelegate(IntPtr self, IntPtr cmd);

        // Static — GC never collects these.
        private static readonly MouseDelegate  s_mouseDown     = OnMouseDown;
        private static readonly MouseDelegate  s_mouseUp       = OnMouseUp;
        private static readonly MouseDelegate  s_mouseDragged  = OnMouseDragged;
        private static readonly MouseDelegate  s_rMouseDown    = OnRightMouseDown;
        private static readonly MouseDelegate  s_rMouseUp      = OnRightMouseUp;
        private static readonly MouseDelegate  s_rMouseDragged = OnRightMouseDragged;
        private static readonly MouseDelegate  s_oMouseDown    = OnOtherMouseDown;
        private static readonly MouseDelegate  s_oMouseUp      = OnOtherMouseUp;
        private static readonly MouseDelegate  s_oMouseDragged = OnOtherMouseDragged;
        private static readonly ScrollDelegate s_scroll        = OnScroll;
        private static readonly KeyDelegate    s_keyDown       = OnKeyDown;
        private static readonly BoolDelegate   s_acceptFirst   = AcceptsFirstResponder;

        private static readonly Lazy<IntPtr> s_class = new(RegisterClass);
        private static readonly ConcurrentDictionary<IntPtr, MTKEventView> s_instances = new();

        // ── ObjC message-sends we need ────────────────────────────────────────────
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern NSPoint NSPoint_msgSend(IntPtr obj, IntPtr sel);

        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern NSPoint NSPoint_msgSend_point_ptr(IntPtr obj, IntPtr sel, NSPoint pt, IntPtr view);

        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern NSRect NSRect_msgSend(IntPtr obj, IntPtr sel);

        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern double Double_msgSend(IntPtr obj, IntPtr sel);

        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern ushort UShort_msgSend(IntPtr obj, IntPtr sel);

        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr IntPtr_msgSend(IntPtr obj, IntPtr sel);

        // Cached selectors
        private static readonly IntPtr sel_locationInWindow  = new Selector("locationInWindow");
        private static readonly IntPtr sel_convertPointFrom  = new Selector("convertPoint:fromView:");
        private static readonly IntPtr sel_bounds            = new Selector("bounds");
        private static readonly IntPtr sel_deltaY            = new Selector("deltaY");
        private static readonly IntPtr sel_scrollingDeltaY   = new Selector("scrollingDeltaY");
        private static readonly IntPtr sel_keyCode           = new Selector("keyCode");
        private static readonly IntPtr sel_buttonNumber      = new Selector("buttonNumber");
        private static readonly IntPtr sel_window             = new Selector("window");
        private static readonly IntPtr sel_backingScaleFactor = new Selector("backingScaleFactor");

        // ── Per-instance callbacks ────────────────────────────────────────────────
        public Action<ViewerMouseButton, int, int>? OnMouseDown_;
        public Action<ViewerMouseButton, int, int>? OnMouseUp_;
        public Action<int, int>?                    OnMouseMove_;
        public Action<int, int, float>?             OnMouseWheel_;
        public Action<ushort>?                      OnKeyDown_;

        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTKEventView v) => v.NativePtr;

        private bool _disposed;

        public MTKEventView(NSRect frame, MTLDevice device)
        {
            NativePtr = new ObjectiveCClass(s_class.Value).Alloc();
            NativePtr = ObjectiveC.IntPtr_objc_msgSend(NativePtr, "initWithFrame:device:", frame, device);
            if (NativePtr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create MTKEventView.");
            s_instances[NativePtr] = this;
        }

        // ── Properties forwarded to MTKView ───────────────────────────────────────
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
        public CAMetalDrawable CurrentDrawable
            => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "currentDrawable"));
        public SharpMetal.Metal.MTLRenderPassDescriptor CurrentRenderPassDescriptor
            => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "currentRenderPassDescriptor"));

        public void MakeFirstResponder()
        {
            var win = IntPtr_msgSend(NativePtr, sel_window);
            if (win != IntPtr.Zero)
                ObjectiveC.objc_msgSend(win, "makeFirstResponder:", NativePtr);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_instances.TryRemove(NativePtr, out _);
            NativePtr = IntPtr.Zero;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static (int x, int y) GetPixelCoords(IntPtr self, IntPtr evt)
        {
            var winPt  = NSPoint_msgSend(evt, sel_locationInWindow);
            var viewPt = NSPoint_msgSend_point_ptr(self, sel_convertPointFrom, winPt, IntPtr.Zero);
            var bounds = NSRect_msgSend(self, sel_bounds);
            // Flip Y: macOS origin is bottom-left, viewport is top-left
            viewPt = new NSPoint(viewPt.X, bounds.Size.Y - viewPt.Y);

            var win   = IntPtr_msgSend(self, sel_window);
            double scale = win != IntPtr.Zero ? Double_msgSend(win, sel_backingScaleFactor) : 1.0;

            return ((int)(viewPt.X * scale), (int)(viewPt.Y * scale));
        }

        // ── Static ObjC callbacks ─────────────────────────────────────────────────

        private static void OnMouseDown(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseDown_?.Invoke(ViewerMouseButton.Left, x, y);
        }
        private static void OnMouseUp(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseUp_?.Invoke(ViewerMouseButton.Left, x, y);
        }
        private static void OnMouseDragged(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseMove_?.Invoke(x, y);
        }
        private static void OnRightMouseDown(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseDown_?.Invoke(ViewerMouseButton.Right, x, y);
        }
        private static void OnRightMouseUp(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseUp_?.Invoke(ViewerMouseButton.Right, x, y);
        }
        private static void OnRightMouseDragged(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseMove_?.Invoke(x, y);
        }
        private static void OnOtherMouseDown(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseDown_?.Invoke(ViewerMouseButton.Middle, x, y);
        }
        private static void OnOtherMouseUp(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseUp_?.Invoke(ViewerMouseButton.Middle, x, y);
        }
        private static void OnOtherMouseDragged(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            inst.OnMouseMove_?.Invoke(x, y);
        }
        private static void OnScroll(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            var (x, y) = GetPixelCoords(self, evt);
            double delta = Double_msgSend(evt, sel_scrollingDeltaY);
            if (delta == 0) delta = Double_msgSend(evt, sel_deltaY);
            inst.OnMouseWheel_?.Invoke(x, y, (float)delta);
        }
        private static void OnKeyDown(IntPtr self, IntPtr cmd, IntPtr evt)
        {
            if (!s_instances.TryGetValue(self, out var inst)) return;
            ushort code = UShort_msgSend(evt, sel_keyCode);
            inst.OnKeyDown_?.Invoke(code);
        }
        private static bool AcceptsFirstResponder(IntPtr self, IntPtr cmd) => true;

        // ── ObjC class registration ───────────────────────────────────────────────

        private static unsafe IntPtr RegisterClass()
        {
            var name     = NullTermUtf8("CloudScopeMTKEventView");
            var voidAtAt = NullTermUtf8("v@:@");
            var boolAt   = NullTermUtf8("B@:");

            fixed (byte* n  = name)
            fixed (byte* va = voidAtAt)
            fixed (byte* ba = boolAt)
            {
                var cls = ObjectiveC.objc_allocateClassPair(
                    new ObjectiveCClass("MTKView"), (char*)n, 0);
                if (cls == IntPtr.Zero)
                    throw new InvalidOperationException("objc_allocateClassPair failed for MTKEventView.");

                void Add(string sel, Delegate d, byte* types) =>
                    ObjectiveC.class_addMethod(cls, sel, Marshal.GetFunctionPointerForDelegate(d), (char*)types);

                Add("mouseDown:",          s_mouseDown,     va);
                Add("mouseUp:",            s_mouseUp,       va);
                Add("mouseDragged:",       s_mouseDragged,  va);
                Add("rightMouseDown:",     s_rMouseDown,    va);
                Add("rightMouseUp:",       s_rMouseUp,      va);
                Add("rightMouseDragged:",  s_rMouseDragged, va);
                Add("otherMouseDown:",     s_oMouseDown,    va);
                Add("otherMouseUp:",       s_oMouseUp,      va);
                Add("otherMouseDragged:",  s_oMouseDragged, va);
                Add("scrollWheel:",        s_scroll,        va);
                Add("keyDown:",            s_keyDown,       va);
                ObjectiveC.class_addMethod(cls, "acceptsFirstResponder",
                    Marshal.GetFunctionPointerForDelegate(s_acceptFirst), (char*)ba);

                ObjectiveC.objc_registerClassPair(cls);
                return cls;
            }
        }

        private static byte[] NullTermUtf8(string s) => Encoding.UTF8.GetBytes(s + '\0');
    }
}
