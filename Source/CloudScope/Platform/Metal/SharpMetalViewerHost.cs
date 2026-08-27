using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CloudScope.Platform.MacOS;
using CloudScope.Platform.MacOS.ObjC;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Rendering;
using CloudScope.Ui;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;
using NSRect = CloudScope.Platform.MacOS.ObjC.NSRect;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    internal sealed class SharpMetalViewerHost : IViewerHost
    {
        private readonly MetalRenderBackend    _renderBackend;
        private readonly ViewerController      _controller;
        private readonly ViewerCommandDispatcher _commandDispatcher;
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
        private bool _controllerLoaded;
        private bool _controllerLoadStarted;
        private readonly ViewerKeyboardState _keyboard = new();

        public SharpMetalViewerHost(int width, int height, MetalRenderBackend renderBackend)
        {
            _renderBackend = renderBackend;
            _controller = new ViewerController(width, height, renderBackend);
            _commandDispatcher = new ViewerCommandDispatcher(_controller);
            ObjectiveC.LinkMetal();
            ObjectiveC.LinkCoreGraphics();
            ObjectiveC.LinkAppKit();
            ObjectiveC.LinkMetalKit();

            _app      = new NSApplication();
            _appDelegate = new NSApplicationDelegate();
            _app.SetDelegate(_appDelegate);

            _appDelegate.OnDidFinishLaunching += _ =>
            {
                MTLDevice device = _renderBackend.Device;
                _device = device;
                _commandQueue = device.NewCommandQueue();

                var rect = new NSRect(100, 100, width, height);
                _mtkView = new MTKEventView(rect, device)
                {
                    ColorPixelFormat        = MTLPixelFormat.BGRA8Unorm,
                    DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
                    SampleCount             = (ulong)_renderBackend.SampleCount,
                    ClearColor              = MetalClearColor.FromPalette(),
                    FramebufferOnly         = false,
                    Paused                  = true,
                    EnableSetNeedsDisplay   = true
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

                    if (!_controllerLoaded)
                    {
                        PresentClearFrame(view, descriptor, drawable);

                        if (!_controllerLoadStarted)
                        {
                            _controllerLoadStarted = true;
                            try
                            {
                                _controller.Load();
                                _controllerLoaded = true;
                                stopwatch.Restart();
                                RequestRedraw();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Load Error] {ex}");
                            }
                        }

                        return;
                    }

                    try
                    {
                        float dt = (float)stopwatch.Elapsed.TotalSeconds;
                        stopwatch.Restart();
                        _controller.UpdateFrame(dt, _keyboard);

                        var cmdBuf = _commandQueue.Value.CommandBuffer();
                        _renderBackend.PrepareFrame(descriptor, drawable, cmdBuf);
                        // The real elapsed time, not zero: the frame diagnostics average over
                        // a second of it, so passing zero left the Metal backend unmeasurable.
                        _controller.RenderFrame(dt);
                        if (_controller.NeedsContinuousFrames)
                            RequestRedraw();
                    }
                    catch (Exception ex) { Console.WriteLine($"[Render Error] {ex}"); }
                    finally
                    {
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
                _mtkView.OnMouseDown_  = (btn, x, y) => { _lastMouseX = x; _lastMouseY = y; _controller.MouseDown(btn, x, y); RequestRedraw(); };
                _mtkView.OnMouseUp_    = (btn, x, y) => { _lastMouseX = x; _lastMouseY = y; _controller.MouseUp(btn, x, y); RequestRedraw(); };
                _mtkView.OnMouseMove_  = (x, y)      => { _lastMouseX = x; _lastMouseY = y; _controller.MouseMove(x, y); RequestRedraw(); };
                _mtkView.OnMouseWheel_ = (x, y, d)   => { _lastMouseX = x; _lastMouseY = y; _controller.MouseWheel(x, y, d); RequestRedraw(); };
                _mtkView.OnKeyDown_    = code         => { HandleKeyDown(code); RequestRedraw(); };
                _mtkView.OnKeyUp_      = code         => { _keyboard.KeyUp(MacKeyCodes.ToViewerKey(code)); RequestRedraw(); };
                RequestRedraw();
            };
        }

        public void Run() => _app.Run();

        public void LoadPointCloud(PointData[] points, float cloudRadius = 50f)
        {
            _controller.LoadPointCloud(points, cloudRadius);
            RequestRedraw();
        }
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

        private void PresentClearFrame(MTKView view, MTLRenderPassDescriptor descriptor, CAMetalDrawable drawable)
        {
            var cmdBuf = _commandQueue?.CommandBuffer() ?? default;
            if (cmdBuf.NativePtr == IntPtr.Zero) return;

            var enc = cmdBuf.RenderCommandEncoder(descriptor);
            if (enc.NativePtr != IntPtr.Zero)
                enc.EndEncoding();

            cmdBuf.PresentDrawable(drawable);
            cmdBuf.Commit();
        }

        private void HandleKeyDown(ushort code)
        {
            var key = MacKeyCodes.ToViewerKey(code);
            if (key == ViewerKey.Unknown) return;
            int mx = _lastMouseX, my = _lastMouseY;
            _keyboard.KeyDown(key);
            bool ctrl = _keyboard.IsKeyDown(ViewerKey.LeftControl) || _keyboard.IsKeyDown(ViewerKey.RightControl);
            if (!_commandDispatcher.TryExecuteShortcut(key, ctrl))
                _controller.KeyDown(key, ctrl, mx, my);
        }

        private void RequestRedraw()
        {
            _mtkView?.SetNeedsDisplay();
        }
    }
}
