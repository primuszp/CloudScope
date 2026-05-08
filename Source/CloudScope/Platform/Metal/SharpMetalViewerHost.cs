using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Rendering;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    internal sealed class SharpMetalViewerHost : IViewerHost
    {
        private readonly ViewerController      _controller;
        private readonly NSApplication         _app;
        private readonly NSApplicationDelegate _appDelegate;
        private MTLDevice?       _device;
        private MTLCommandQueue? _commandQueue;

        private MTKViewDelegate? _viewDelegate;
        private MTKEventView?    _mtkView;
        private NSWindow?        _window;
        private int _drawableWidth;
        private int _drawableHeight;
        private int _lastMouseX;
        private int _lastMouseY;
        private readonly RealKeyboard _keyboard = new();
        private readonly ConcurrentQueue<Action> _inputQueue = new();

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

            _appDelegate.OnDidFinishLaunching += _ =>
            {
                var device = MTLDevice.CreateSystemDefaultDevice();
                _device = device;
                _commandQueue = device.NewCommandQueue();
                MetalFrameContext.Initialize(device, _commandQueue.Value);

                var rect = new NSRect(100, 100, width, height);
                _mtkView = new MTKEventView(rect, device)
                {
                    ColorPixelFormat        = MTLPixelFormat.BGRA8Unorm,
                    DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
                    ClearColor              = new MTLClearColor { red = 0.15, green = 0.15, blue = 0.15, alpha = 1.0 },
                    FramebufferOnly         = false
                };

                ulong style = (ulong)(NSStyleMask.Titled | NSStyleMask.Closable | NSStyleMask.Resizable | NSStyleMask.Miniaturizable);
                _window = new NSWindow(rect, style);
                _window.SetContentView(_mtkView.NativePtr);
                _window.Title = (NSString)"CloudScope Metal Viewer";
                _window.MakeKeyAndOrderFront();

                _mtkView.MakeFirstResponder();

                int frameCount = 0;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                _viewDelegate = new MTKViewDelegate();
                _viewDelegate.OnDraw_ = view =>
                {
                    var descriptor = view.CurrentRenderPassDescriptor;
                    var drawable   = view.CurrentDrawable;

                    if (descriptor.NativePtr == IntPtr.Zero || drawable.NativePtr == IntPtr.Zero) return;

                    SyncDrawableSizeFromRenderPass(descriptor);

                    try
                    {
                        while (_inputQueue.TryDequeue(out var inputAction))
                        {
                            try { inputAction(); }
                            catch (Exception ex) { Console.WriteLine($"[Input Error] {ex.GetType().Name}: {ex.Message}"); }
                        }

                        float dt = (float)stopwatch.Elapsed.TotalSeconds;
                        stopwatch.Restart();
                        _controller.UpdateFrame(dt, _keyboard);

                        var cmdBuf = _commandQueue.Value.CommandBuffer();
                        MetalFrameContext.Begin(view, descriptor, drawable, cmdBuf);
                        _controller.RenderFrame(0);
                    }
                    catch (Exception ex) { Console.WriteLine($"[Render Error] {ex}"); }
                    finally
                    {
                        var enc = MetalFrameContext.CurrentRenderCommandEncoder;
                        if (enc.NativePtr != IntPtr.Zero)
                            enc.EndEncoding();

                        var cmdBuf = MetalFrameContext.CurrentCommandBuffer;
                        if (cmdBuf.NativePtr != IntPtr.Zero)
                        {
                            cmdBuf.PresentDrawable(drawable);
                            cmdBuf.Commit();
                            cmdBuf.WaitUntilCompleted();
                        }
                        
                        MetalFrameContext.End();
                        frameCount++;
                    }
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
                _mtkView.OnMouseDown_  = (btn, x, y) => { _lastMouseX = x; _lastMouseY = y; _inputQueue.Enqueue(() => _controller.MouseDown(btn, x, y)); };
                _mtkView.OnMouseUp_    = (btn, x, y) => { _lastMouseX = x; _lastMouseY = y; _inputQueue.Enqueue(() => _controller.MouseUp(btn, x, y)); };
                _mtkView.OnMouseMove_  = (x, y)      => { _lastMouseX = x; _lastMouseY = y; _inputQueue.Enqueue(() => _controller.MouseMove(x, y)); };
                _mtkView.OnMouseWheel_ = (x, y, d)   => { _lastMouseX = x; _lastMouseY = y; _inputQueue.Enqueue(() => _controller.MouseWheel(x, y, d)); };
                _mtkView.OnKeyDown_    = code         => EnqueueKey(code);
                _mtkView.OnKeyUp_      = code         => { var k = MapKey(code); _inputQueue.Enqueue(() => _keyboard.KeyUp(k)); };

                _controller.Load();
            };
        }

        public void Run() => _app.Run();

        public void LoadPointCloud(PointData[] points, float cloudRadius = 50f) => _controller.LoadPointCloud(points, cloudRadius);
        public void SetLasFilePath(string path) => _controller.SetLasFilePath(path);
        public void Dispose() => _controller.Dispose();

        private void SyncDrawableSizeFromRenderPass(MTLRenderPassDescriptor desc)
        {
            var tex = desc.ColorAttachments.Object(0).Texture;
            if (tex.NativePtr == IntPtr.Zero) return;
            int w = (int)tex.Width, h = (int)tex.Height;
            if (w == _drawableWidth && h == _drawableHeight) return;
            _mtkView?.UpdateDrawableSize(w, h);
            _controller.Resize(w, h);
            _drawableWidth = w;
            _drawableHeight = h;
        }

        private void EnqueueKey(ushort code)
        {
            var key = MapKey(code);
            if (key == ViewerKey.Unknown) return;
            int mx = _lastMouseX, my = _lastMouseY;
            _inputQueue.Enqueue(() =>
            {
                _keyboard.KeyDown(key);
                bool ctrl = _keyboard.IsKeyDown(ViewerKey.LeftControl) || _keyboard.IsKeyDown(ViewerKey.RightControl);
                _controller.KeyDown(key, ctrl, mx, my);
            });
        }

        private static ViewerKey MapKey(ushort code) => code switch
        {
            53  => ViewerKey.Escape,    49  => ViewerKey.Space,
            36  => ViewerKey.Enter,     56  => ViewerKey.LeftShift,
            60  => ViewerKey.RightShift, 59 => ViewerKey.LeftControl,
            62  => ViewerKey.RightControl,
            12  => ViewerKey.Q,         13  => ViewerKey.W,
            14  => ViewerKey.E,         0   => ViewerKey.A,
            1   => ViewerKey.S,         2   => ViewerKey.D,
            69  => ViewerKey.KeyPadAdd, 78  => ViewerKey.KeyPadSubtract,
            71  => ViewerKey.KeyPad7,   77  => ViewerKey.KeyPad3,
            65  => ViewerKey.KeyPad1,   87  => ViewerKey.KeyPad5,
            115 => ViewerKey.Home,      3   => ViewerKey.F,
            _   => ViewerKey.Unknown
        };

        private class RealKeyboard : IViewerKeyboard
        {
            private readonly System.Collections.Generic.HashSet<ViewerKey> _down = new();
            public void KeyDown(ViewerKey key) => _down.Add(key);
            public void KeyUp(ViewerKey key)   => _down.Remove(key);
            public bool IsKeyDown(ViewerKey key) => _down.Contains(key);
            public bool IsKeyPressed(ViewerKey key) => _down.Contains(key);
        }
    }
}
