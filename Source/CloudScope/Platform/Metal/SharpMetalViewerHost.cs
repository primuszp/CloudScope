using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Rendering;
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

        // Kept as fields to prevent GC collection.
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
                _window.Title = (SharpMetal.Foundation.NSString)"CloudScope - Point Cloud Viewer";

                _mtkView = new MTKEventView(rect, device)
                {
                    ColorPixelFormat        = MTLPixelFormat.BGRA8Unorm,
                    DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
                    ClearColor              = new MTLClearColor { red = 0.015, green = 0.018, blue = 0.022, alpha = 1.0 },
                    FramebufferOnly         = false, // must be false to allow depth reads
                    Paused                  = false,
                    EnableSetNeedsDisplay   = false
                };

                // ── Draw delegate ─────────────────────────────────────────────────
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var dummyKeyboard = new DummyKeyboard();
                int frameCount = 0;

                _viewDelegate = new MTKViewDelegate();
                _viewDelegate.OnDraw_ = view =>
                {
                    var descriptor = view.CurrentRenderPassDescriptor;
                    var drawable = view.CurrentDrawable;

                    if (descriptor.NativePtr == IntPtr.Zero)
                    {
                        if (frameCount < 10) Console.WriteLine($"[Metal] Frame {frameCount}: descriptor is NULL");
                        frameCount++;
                        return;
                    }
                    if (drawable.NativePtr == IntPtr.Zero)
                    {
                        if (frameCount < 10) Console.WriteLine($"[Metal] Frame {frameCount}: drawable is NULL");
                        frameCount++;
                        return;
                    }

                    SyncDrawableSizeFromRenderPass(descriptor);

                    var cmdBuffer = MetalFrameContext.CommandQueue.CommandBuffer();
                    if (cmdBuffer.NativePtr == IntPtr.Zero)
                    {
                        Console.WriteLine($"[Metal] Frame {frameCount}: commandBuffer is NULL");
                        frameCount++;
                        return;
                    }

                    MetalFrameContext.Begin(view, descriptor, drawable, cmdBuffer);
                    try
                    {
                        float dt = (float)stopwatch.Elapsed.TotalSeconds;
                        stopwatch.Restart();
                        _controller.UpdateFrame(dt, dummyKeyboard);
                        _controller.RenderFrame(0);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Metal] Frame {frameCount} exception: {ex.Message}");
                    }
                    finally
                    {
                        var enc = MetalFrameContext.CurrentRenderCommandEncoder;
                        if (enc.NativePtr != IntPtr.Zero)
                            enc.EndEncoding();

                        // CRITICAL: PresentDrawable must be called BEFORE Commit
                        cmdBuffer.PresentDrawable(drawable);
                        cmdBuffer.Commit();
                        MetalFrameContext.End();
                        frameCount++;
                        if (frameCount < 5 || frameCount % 120 == 0)
                            Console.WriteLine($"[F{frameCount}] committed t={stopwatch.Elapsed.TotalSeconds:F1}s");
                    }
                };
                _viewDelegate.OnSizeChange_ = (_, size) =>
                {
                    int w = (int)size.Width, h = (int)size.Height;
                    if (w <= 0 || h <= 0)
                        return;

                    _mtkView?.UpdateDrawableSize(w, h);
                    _controller.Resize(w, h);
                    _drawableWidth = w;
                    _drawableHeight = h;
                };

                _mtkView.Delegate = _viewDelegate;

                // ── Input events ──────────────────────────────────────────────────
                _mtkView.OnMouseDown_  = (btn, x, y) => _controller.MouseDown(btn, x, y);
                _mtkView.OnMouseUp_    = (btn, x, y) => _controller.MouseUp(btn, x, y);
                _mtkView.OnMouseMove_  = (x, y)      => _controller.MouseMove(x, y);
                _mtkView.OnMouseWheel_ = (x, y, d)   => _controller.MouseWheel(x, y, d);
                _mtkView.OnKeyDown_    = code         => HandleKey(code);

                // ── Show ──────────────────────────────────────────────────────────
                _controller.Load();

                _window.SetContentView(_mtkView);
                _window.MakeKeyAndOrderFront();
                _app.ActivateIgnoringOtherApps(true);
                _mtkView.MakeFirstResponder();
            };
        }

        public SharpMetalViewerHost(int width, int height)
            : this(width, height, new MetalRenderBackend())
        {
        }

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
            if (texture.NativePtr == IntPtr.Zero)
                return;

            int width = checked((int)texture.Width);
            int height = checked((int)texture.Height);
            if (width <= 0 || height <= 0)
                return;

            if (width == _drawableWidth && height == _drawableHeight)
                return;

            _mtkView?.UpdateDrawableSize(width, height);
            _controller.Resize(width, height);
            _drawableWidth = width;
            _drawableHeight = height;
        }

        // ── Key mapping ───────────────────────────────────────────────────────────

        private void HandleKey(ushort code)
        {
            var key = MapKey(code);
            if (key == ViewerKey.Unknown) return;
            // We don't have a live mouse pos here — use center as fallback.
            bool ctrl = IsModifierDown(59) || IsModifierDown(62); // left/right Ctrl
            _controller.KeyDown(key, ctrl, 0, 0);
        }

        // macOS virtual key codes
        private static ViewerKey MapKey(ushort code) => code switch
        {
            53  => ViewerKey.Escape,
            49  => ViewerKey.Space,
            36  => ViewerKey.Enter,
            56  => ViewerKey.LeftShift,
            60  => ViewerKey.RightShift,
            59  => ViewerKey.LeftControl,
            62  => ViewerKey.RightControl,
            // Letter keys
            0   => ViewerKey.A,
            1   => ViewerKey.S,
            2   => ViewerKey.D,
            3   => ViewerKey.F,
            5   => ViewerKey.G,
            6   => ViewerKey.Z,
            7   => ViewerKey.X,
            12  => ViewerKey.Q,
            13  => ViewerKey.W,
            14  => ViewerKey.E,
            15  => ViewerKey.R,
            16  => ViewerKey.Y,
            31  => ViewerKey.O,
            37  => ViewerKey.L,
            // Number row
            18  => ViewerKey.D1,
            19  => ViewerKey.D2,
            20  => ViewerKey.D3,
            21  => ViewerKey.D4,
            23  => ViewerKey.D5,
            22  => ViewerKey.D6,
            26  => ViewerKey.D7,
            28  => ViewerKey.D8,
            25  => ViewerKey.D9,
            // Numpad
            83  => ViewerKey.KeyPad1,
            85  => ViewerKey.KeyPad3,
            86  => ViewerKey.KeyPad5,
            89  => ViewerKey.KeyPad7,
            69  => ViewerKey.KeyPadAdd,
            78  => ViewerKey.KeyPadSubtract,
            // Home
            115 => ViewerKey.Home,
            _   => ViewerKey.Unknown
        };

        [System.Runtime.InteropServices.DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern ushort GetModifierFlags(IntPtr obj, IntPtr sel);

        private static bool IsModifierDown(ushort keyCode)
        {
            // Simple approach: check NSEvent.modifierFlags via CGEventSourceKeyState would be
            // more accurate, but for Ctrl detection during keyDown we can skip for now.
            return false;
        }

        private class DummyKeyboard : IViewerKeyboard
        {
            public bool IsKeyDown(ViewerKey key) => false;
            public bool IsKeyPressed(ViewerKey key) => false;
        }
    }
}
