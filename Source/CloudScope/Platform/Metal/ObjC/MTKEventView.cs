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
    [SupportedOSPlatform("macos")]
    internal sealed class MTKEventView : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MouseDelegate(IntPtr self, IntPtr cmd, IntPtr evt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ScrollDelegate(IntPtr self, IntPtr cmd, IntPtr evt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void KeyDelegate(IntPtr self, IntPtr cmd, IntPtr evt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool BoolDelegate(IntPtr self, IntPtr cmd);

        private static readonly MouseDelegate  s_mouseDown     = OnMouseDown;
        private static readonly MouseDelegate  s_mouseUp       = OnMouseUp;
        private static readonly MouseDelegate  s_mouseDragged  = OnMouseDragged;
        private static readonly MouseDelegate  s_rMouseDown    = OnRightMouseDown;
        private static readonly MouseDelegate  s_rMouseUp      = OnRightMouseUp;
        private static readonly MouseDelegate  s_rMouseDragged = OnRightMouseDragged;
        private static readonly MouseDelegate  s_oMouseDown    = OnOtherMouseDown;
        private static readonly MouseDelegate  s_oMouseUp      = OnOtherMouseUp;
        private static readonly MouseDelegate  s_oMouseDragged = OnOtherMouseDragged;
        private static readonly MouseDelegate  s_mouseMoved    = OnMouseMoved;
        private static readonly ScrollDelegate s_scroll        = OnScroll;
        private static readonly KeyDelegate    s_keyDown       = OnKeyDown;
        private static readonly KeyDelegate    s_keyUp         = OnKeyUp;
        private static readonly BoolDelegate   s_acceptFirst   = AcceptsFirstResponder;

        private static readonly Lazy<IntPtr> s_class = new(RegisterClass);
        private static readonly ConcurrentDictionary<IntPtr, MTKEventView> s_instances = new();

        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern NSPoint NSPoint_msgSend(IntPtr obj, IntPtr sel);
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern double Double_msgSend(IntPtr obj, IntPtr sel);
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern ushort UShort_msgSend(IntPtr obj, IntPtr sel);
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr IntPtr_msgSend(IntPtr obj, IntPtr sel);

        private static readonly IntPtr sel_locationInWindow   = new Selector("locationInWindow");
        private static readonly IntPtr sel_deltaY             = new Selector("deltaY");
        private static readonly IntPtr sel_scrollingDeltaY    = new Selector("scrollingDeltaY");
        private static readonly IntPtr sel_keyCode            = new Selector("keyCode");
        private static readonly IntPtr sel_window             = new Selector("window");
        private static readonly IntPtr sel_backingScaleFactor = new Selector("backingScaleFactor");

        public Action<ViewerMouseButton, int, int>? OnMouseDown_;
        public Action<ViewerMouseButton, int, int>? OnMouseUp_;
        public Action<int, int>?                    OnMouseMove_;
        public Action<int, int, float>?             OnMouseWheel_;
        public Action<ushort>?                      OnKeyDown_;
        public Action<ushort>?                      OnKeyUp_;

        private int    _drawableHeight;
        private double _pixelScale = 1.0;

        public IntPtr NativePtr;
        public static implicit operator IntPtr(MTKEventView v) => v.NativePtr;
        private bool _disposed;

        public MTKEventView(NSRect frame, MTLDevice device)
        {
            _ = s_class.Value;
            var ptr = new ObjectiveCClass("CloudScopeMTKEventView").Alloc();
            NativePtr = ObjectiveC.IntPtr_objc_msgSend(ptr, "initWithFrame:device:", frame, device);
            if (NativePtr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create MTKEventView.");
            _drawableHeight = (int)frame.Size.Y;
            s_instances[NativePtr] = this;
            Console.WriteLine($"[MTKEventView] Created: {NativePtr}");
        }

        public MTLPixelFormat ColorPixelFormat { set => ObjectiveC.objc_msgSend(NativePtr, new Selector("setColorPixelFormat:"), value); }
        public MTLPixelFormat DepthStencilPixelFormat { set => ObjectiveC.objc_msgSend(NativePtr, new Selector("setDepthStencilPixelFormat:"), value); }
        public MTLClearColor ClearColor { set => ObjectiveC.objc_msgSend(NativePtr, new Selector("setClearColor:"), value); }
        public MTKViewDelegate? Delegate { set => ObjectiveC.objc_msgSend(NativePtr, "setDelegate:", value?.NativePtr ?? IntPtr.Zero); }
        public bool FramebufferOnly { set => ObjectiveC.objc_msgSend(NativePtr, "setFramebufferOnly:", value); }
        public bool Paused { set => ObjectiveC.objc_msgSend(NativePtr, "setPaused:", value); }
        public bool EnableSetNeedsDisplay { set => ObjectiveC.objc_msgSend(NativePtr, "setEnableSetNeedsDisplay:", value); }
        public CAMetalDrawable CurrentDrawable => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "currentDrawable"));
        public MTLRenderPassDescriptor CurrentRenderPassDescriptor => new(ObjectiveC.IntPtr_objc_msgSend(NativePtr, "currentRenderPassDescriptor"));

        public void MakeFirstResponder()
        {
            var win = IntPtr_msgSend(NativePtr, sel_window);
            if (win != IntPtr.Zero)
            {
                _pixelScale = Double_msgSend(win, sel_backingScaleFactor);
                ObjectiveC.objc_msgSend(win, "makeFirstResponder:", NativePtr);
                Console.WriteLine($"[MTKEventView] MakeFirstResponder on window {win}, pixelScale={_pixelScale}");
            }
        }

        public void UpdateDrawableSize(int width, int height)
        {
            _drawableHeight = height;
            var win = IntPtr_msgSend(NativePtr, sel_window);
            if (win != IntPtr.Zero)
                _pixelScale = Double_msgSend(win, sel_backingScaleFactor);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_instances.TryRemove(NativePtr, out _);
            NativePtr = IntPtr.Zero;
        }

        private (int x, int y) GetPixelCoords(IntPtr evt)
        {
            var pt = NSPoint_msgSend(evt, sel_locationInWindow);
            int x = (int)(pt.X * _pixelScale);
            int y = _drawableHeight - (int)(pt.Y * _pixelScale);
            return (x, Math.Clamp(y, 0, _drawableHeight));
        }

        private static void OnMouseDown(IntPtr self, IntPtr cmd, IntPtr evt) { Console.WriteLine("[Input] MouseDown"); if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseDown_?.Invoke(ViewerMouseButton.Left, x, y); } }
        private static void OnMouseUp(IntPtr self, IntPtr cmd, IntPtr evt) { Console.WriteLine("[Input] MouseUp"); if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseUp_?.Invoke(ViewerMouseButton.Left, x, y); } }
        private static void OnMouseDragged(IntPtr self, IntPtr cmd, IntPtr evt) { if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseMove_?.Invoke(x, y); } }
        private static void OnRightMouseDown(IntPtr self, IntPtr cmd, IntPtr evt) { Console.WriteLine("[Input] RMouseDown"); if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseDown_?.Invoke(ViewerMouseButton.Right, x, y); } }
        private static void OnRightMouseUp(IntPtr self, IntPtr cmd, IntPtr evt) { Console.WriteLine("[Input] RMouseUp"); if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseUp_?.Invoke(ViewerMouseButton.Right, x, y); } }
        private static void OnRightMouseDragged(IntPtr self, IntPtr cmd, IntPtr evt) { if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseMove_?.Invoke(x, y); } }
        private static void OnOtherMouseDown(IntPtr self, IntPtr cmd, IntPtr evt) { if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseDown_?.Invoke(ViewerMouseButton.Middle, x, y); } }
        private static void OnOtherMouseUp(IntPtr self, IntPtr cmd, IntPtr evt) { if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseUp_?.Invoke(ViewerMouseButton.Middle, x, y); } }
        private static void OnOtherMouseDragged(IntPtr self, IntPtr cmd, IntPtr evt) { if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseMove_?.Invoke(x, y); } }
        private static void OnMouseMoved(IntPtr self, IntPtr cmd, IntPtr evt) { if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); inst.OnMouseMove_?.Invoke(x, y); } }
        private static void OnScroll(IntPtr self, IntPtr cmd, IntPtr evt) { Console.WriteLine("[Input] Scroll"); if (s_instances.TryGetValue(self, out var inst)) { var (x, y) = inst.GetPixelCoords(evt); double delta = Double_msgSend(evt, sel_scrollingDeltaY); if (delta == 0.0) delta = Double_msgSend(evt, sel_deltaY); inst.OnMouseWheel_?.Invoke(x, y, (float)delta); } }
        private static void OnKeyDown(IntPtr self, IntPtr cmd, IntPtr evt) { ushort code = UShort_msgSend(evt, sel_keyCode); Console.WriteLine($"[Input] KeyDown: {code}"); if (s_instances.TryGetValue(self, out var inst)) { inst.OnKeyDown_?.Invoke(code); } }
        private static void OnKeyUp(IntPtr self, IntPtr cmd, IntPtr evt) { ushort code = UShort_msgSend(evt, sel_keyCode); if (s_instances.TryGetValue(self, out var inst)) { inst.OnKeyUp_?.Invoke(code); } }
        private static bool AcceptsFirstResponder(IntPtr self, IntPtr cmd) { Console.WriteLine("[Input] AcceptsFirstResponder: YES"); return true; }

        private static unsafe IntPtr RegisterClass()
        {
            var name = NullUtf8("CloudScopeMTKEventView");
            var va = NullUtf8("v@:@");
            var ba = NullUtf8("B@:");
            fixed (byte* n = name) fixed (byte* v_a = va) fixed (byte* b_a = ba)
            {
                var cls = ObjectiveC.objc_allocateClassPair(new ObjectiveCClass("MTKView"), (char*)n, 0);
                if (cls == IntPtr.Zero) Console.WriteLine("[MTKEventView] Class registration FAILED or already exists");
                else Console.WriteLine("[MTKEventView] Class registration OK");

                void Add(string sel, Delegate d, byte* t) => ObjectiveC.class_addMethod(cls, sel, Marshal.GetFunctionPointerForDelegate(d), (char*)t);
                Add("mouseDown:", s_mouseDown, v_a); Add("mouseUp:", s_mouseUp, v_a); Add("mouseDragged:", s_mouseDragged, v_a);
                Add("rightMouseDown:", s_rMouseDown, v_a); Add("rightMouseUp:", s_rMouseUp, v_a); Add("rightMouseDragged:", s_rMouseDragged, v_a);
                Add("otherMouseDown:", s_oMouseDown, v_a); Add("otherMouseUp:", s_oMouseUp, v_a); Add("otherMouseDragged:", s_oMouseDragged, v_a);
                Add("mouseMoved:", s_mouseMoved, v_a); Add("scrollWheel:", s_scroll, v_a); Add("keyDown:", s_keyDown, v_a); Add("keyUp:", s_keyUp, v_a);
                ObjectiveC.class_addMethod(cls, "acceptsFirstResponder", Marshal.GetFunctionPointerForDelegate(s_acceptFirst), (char*)b_a);
                ObjectiveC.objc_registerClassPair(cls);
                return cls;
            }
        }
        private static byte[] NullUtf8(string s) => Encoding.UTF8.GetBytes(s + '\0');
    }
}
