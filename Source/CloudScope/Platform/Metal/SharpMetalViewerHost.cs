using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Rendering;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class SharpMetalViewerHost : IViewerHost
    {
        private readonly ViewerController      _controller;
        private readonly NSApplication         _app;
        private readonly NSApplicationDelegate _appDelegate;

        private MTKViewDelegate? _viewDelegate;
        private MTKEventView?    _mtkView;
        private NSWindow?        _window;
        private int _drawableWidth;
        private int _drawableHeight;

        public SharpMetalViewerHost(int width, int height, IRenderBackend renderBackend)
        {
            _controller = new ViewerController(width, height, renderBackend);

            ObjectiveC.LinkMetal();
            ObjectiveC.LinkCoreGraphics();
            ObjectiveC.LinkAppKit();
            ObjectiveC.LinkMetalKit();

            _app      = new NSApplication();
            _appDelegate = new NSApplicationDelegate();
            _app.SetDelegate(_appDelegate);

            _appDelegate.OnWillFinishLaunching += _ =>
                _app.SetActivationPolicy(NSApplicationActivationPolicy.Regular);

            _appDelegate.OnDidFinishLaunching += _ =>
            {
                var device       = MTLDevice.CreateSystemDefaultDevice();
                var commandQueue = device.NewCommandQueue();
                MetalFrameContext.Initialize(device, commandQueue);

                var rect = new NSRect(100, 100, width, height);

                _window = new NSWindow(rect,
                    (ulong)(NSStyleMask.Titled | NSStyleMask.Closable |
                            NSStyleMask.Miniaturizable | NSStyleMask.Resizable));
                _window.Title = (NSString)"CloudScope - Point Cloud Viewer";

                _mtkView = new MTKEventView(rect, device)
                {
                    ColorPixelFormat        = MTLPixelFormat.BGRA8Unorm,
                    DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
                    ClearColor              = new MTLClearColor { red = 0.015, green = 0.018, blue = 0.022, alpha = 1.0 },
                    FramebufferOnly         = false,
                    Paused                  = false,
                    EnableSetNeedsDisplay   = false
                };

                // ── DIAGNOSTIC: direct triangle (same as MetalTriangleTest) ──────
                var diagPipeline = CreateMinimalTrianglePipeline(device);
                Console.WriteLine($"[Init] diagPipeline: {(diagPipeline.NativePtr != IntPtr.Zero ? "OK" : "FAIL")}");

                int frameCount = 0;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var dummyKeyboard = new DummyKeyboard();

                _viewDelegate = new MTKViewDelegate();
                _viewDelegate.OnDraw_ = view =>
                {
                    var descriptor = view.CurrentRenderPassDescriptor;
                    var drawable   = view.CurrentDrawable;
                    if (descriptor.NativePtr == IntPtr.Zero || drawable.NativePtr == IntPtr.Zero)
                    {
                        if (frameCount < 5) Console.WriteLine($"[F{frameCount}] NULL desc/drawable");
                        frameCount++; return;
                    }

                    // DIRECT path - identical to MetalTriangleTest, no MetalFrameContext
                    var cmdBuf  = commandQueue.CommandBuffer();
                    var encoder = cmdBuf.RenderCommandEncoder(descriptor);

                    if (diagPipeline.NativePtr != IntPtr.Zero)
                    {
                        encoder.SetRenderPipelineState(diagPipeline);
                        encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
                    }

                    encoder.EndEncoding();
                    cmdBuf.PresentDrawable(drawable);
                    cmdBuf.Commit();

                    if (frameCount < 5 || frameCount % 120 == 0)
                        Console.WriteLine($"[F{frameCount}] OK t={stopwatch.Elapsed.TotalSeconds:F1}s");
                    frameCount++;
                };

                _viewDelegate.OnSizeChange_ = (_, size) =>
                {
                    int w = (int)size.Width, h = (int)size.Height;
                    if (w <= 0 || h <= 0) return;
                    _mtkView?.UpdateDrawableSize(w, h);
                    _controller.Resize(w, h);
                    _drawableWidth = w;
                    _drawableHeight = h;
                };

                _mtkView.Delegate = _viewDelegate;

                _mtkView.OnMouseDown_  = (btn, x, y) => _controller.MouseDown(btn, x, y);
                _mtkView.OnMouseUp_    = (btn, x, y) => _controller.MouseUp(btn, x, y);
                _mtkView.OnMouseMove_  = (x, y)      => _controller.MouseMove(x, y);
                _mtkView.OnMouseWheel_ = (x, y, d)   => _controller.MouseWheel(x, y, d);
                _mtkView.OnKeyDown_    = code         => HandleKey(code);

                _controller.Load();

                _window.SetContentView(_mtkView);
                _window.MakeKeyAndOrderFront();
                _app.ActivateIgnoringOtherApps(true);
                _mtkView.MakeFirstResponder();
            };
        }

        // ── Minimal triangle pipeline (for diagnostic) ────────────────────────────
        static MTLRenderPipelineState CreateMinimalTrianglePipeline(MTLDevice device)
        {
            const string src = @"
#include <metal_stdlib>
using namespace metal;
struct V { float4 pos [[position]]; };
vertex V tv(uint id [[vertex_id]]) {
    float2 p[3] = { {0,0.6}, {-0.6,-0.6}, {0.6,-0.6} };
    return { float4(p[id],0,1) };
}
fragment float4 tf(V in [[stage_in]]) { return float4(1,0,0,1); }
";
            var libErr = new NSError(IntPtr.Zero);
            var lib    = device.NewLibrary((NSString)src, new MTLCompileOptions(IntPtr.Zero), ref libErr);
            if (lib.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine($"[Diag shader] FAIL: {libErr.LocalizedDescription}");
                return default;
            }
            var desc = new MTLRenderPipelineDescriptor();
            desc.VertexFunction   = lib.NewFunction((NSString)"tv");
            desc.FragmentFunction = lib.NewFunction((NSString)"tf");
            desc.DepthAttachmentPixelFormat = MTLPixelFormat.Depth32Float;
            var ca = desc.ColorAttachments.Object(0);
            ca.PixelFormat = MTLPixelFormat.BGRA8Unorm;
            desc.ColorAttachments.SetObject(ca, 0);
            var pipeErr = new NSError(IntPtr.Zero);
            var state   = device.NewRenderPipelineState(desc, ref pipeErr);
            if (pipeErr.NativePtr != IntPtr.Zero) Console.WriteLine($"[Diag pipeline] FAIL: {pipeErr.LocalizedDescription}");
            return state;
        }

        public SharpMetalViewerHost(int width, int height)
            : this(width, height, new MetalRenderBackend()) { }

        public void LoadPointCloud(PointData[] points, float cloudRadius = 50f)
            => _controller.LoadPointCloud(points, cloudRadius);

        public void SetLasFilePath(string path)
            => _controller.SetLasFilePath(path);

        public void Run() => _app.Run();

        public void Dispose()
        {
            _viewDelegate?.Dispose();
            _mtkView?.Dispose();
            _controller.Dispose();
        }

        private void SyncDrawableSizeFromRenderPass(MTLRenderPassDescriptor descriptor)
        {
            var color = descriptor.ColorAttachments.Object(0);
            var texture = color.Texture;
            if (texture.NativePtr == IntPtr.Zero) return;
            int w = checked((int)texture.Width);
            int h = checked((int)texture.Height);
            if (w <= 0 || h <= 0) return;
            if (w == _drawableWidth && h == _drawableHeight) return;
            _mtkView?.UpdateDrawableSize(w, h);
            _controller.Resize(w, h);
            _drawableWidth = w;
            _drawableHeight = h;
        }

        private void HandleKey(ushort code)
        {
            var key = MapKey(code);
            if (key == ViewerKey.Unknown) return;
            bool ctrl = IsModifierDown(59) || IsModifierDown(62);
            _controller.KeyDown(key, ctrl, 0, 0);
        }

        private static ViewerKey MapKey(ushort code) => code switch
        {
            53  => ViewerKey.Escape,    49  => ViewerKey.Space,
            36  => ViewerKey.Enter,     56  => ViewerKey.LeftShift,
            60  => ViewerKey.RightShift, 59 => ViewerKey.LeftControl,
            62  => ViewerKey.RightControl,
            0   => ViewerKey.A, 1 => ViewerKey.S, 2 => ViewerKey.D,
            3   => ViewerKey.F, 5 => ViewerKey.G, 6 => ViewerKey.Z,
            7   => ViewerKey.X, 12 => ViewerKey.Q, 13 => ViewerKey.W,
            14  => ViewerKey.E, 15 => ViewerKey.R, 16 => ViewerKey.Y,
            31  => ViewerKey.O, 37 => ViewerKey.L,
            18  => ViewerKey.D1, 19 => ViewerKey.D2, 20 => ViewerKey.D3,
            21  => ViewerKey.D4, 23 => ViewerKey.D5, 22 => ViewerKey.D6,
            26  => ViewerKey.D7, 28 => ViewerKey.D8, 25 => ViewerKey.D9,
            83  => ViewerKey.KeyPad1, 85 => ViewerKey.KeyPad3,
            86  => ViewerKey.KeyPad5, 89 => ViewerKey.KeyPad7,
            69  => ViewerKey.KeyPadAdd, 78 => ViewerKey.KeyPadSubtract,
            115 => ViewerKey.Home,
            _   => ViewerKey.Unknown
        };

        [System.Runtime.InteropServices.DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern ushort GetModifierFlags(IntPtr obj, IntPtr sel);

        private static bool IsModifierDown(ushort keyCode) => false;

        private class DummyKeyboard : IViewerKeyboard
        {
            public bool IsKeyDown(ViewerKey key) => false;
            public bool IsKeyPressed(ViewerKey key) => false;
        }
    }
}
