using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Rendering;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace CloudScope.Platform.Metal
{
    /// <summary>
    /// Metal viewer host built entirely on SharpMetal + ObjC runtime.
    /// No net9.0-macos / Xamarin SDK required.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public sealed class SharpMetalViewerHost : IViewerHost
    {
        private const int DefaultWidth = 1600;
        private const int DefaultHeight = 900;

        private readonly ViewerController _controller;
        private readonly NSApplication _app;
        private readonly NSApplicationDelegate _appDelegate;

        public SharpMetalViewerHost(int width, int height, IRenderBackend renderBackend)
        {
            _controller = new ViewerController(width, height, renderBackend);

            ObjectiveC.LinkMetal();
            ObjectiveC.LinkCoreGraphics();
            ObjectiveC.LinkAppKit();
            ObjectiveC.LinkMetalKit();

            _app = new NSApplication();
            _appDelegate = new NSApplicationDelegate();
            _app.SetDelegate(_appDelegate);

            _appDelegate.OnWillFinishLaunching += _ =>
            {
                _app.SetActivationPolicy(0); // NSApplicationActivationPolicyRegular
            };

            _appDelegate.OnDidFinishLaunching += _ =>
            {
                var device = MTLDevice.CreateSystemDefaultDevice();
                var commandQueue = device.NewCommandQueue();
                MetalFrameContext.Initialize(device, commandQueue);

                var rect = new NSRect(100, 100, width, height);
                var window = new NSWindow(rect,
                    (ulong)(NSStyleMask.Titled | NSStyleMask.Closable |
                            NSStyleMask.Miniaturizable | NSStyleMask.Resizable));
                window.Title = (SharpMetal.Foundation.NSString)"CloudScope - Point Cloud Viewer";

                var mtkView = new ObjC.MTKView(rect, device)
                {
                    ColorPixelFormat = MTLPixelFormat.BGRA8Unorm,
                    DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
                    ClearColor = new MTLClearColor { red = 0.015, green = 0.018, blue = 0.022, alpha = 1.0 }
                };

                var viewDelegate = new MTKViewDelegate();
                viewDelegate.OnDraw = view =>
                {
                    var cmdBuffer = MetalFrameContext.CommandQueue.CommandBuffer();
                    MetalFrameContext.Begin(view, cmdBuffer);
                    try
                    {
                        _controller.RenderFrame(0);
                        var drawable = view.CurrentDrawable;
                        if (drawable.NativePtr != IntPtr.Zero)
                            cmdBuffer.PresentDrawable(drawable);
                        cmdBuffer.Commit();
                    }
                    finally
                    {
                        MetalFrameContext.End();
                    }
                };
                viewDelegate.OnSizeChange = (view, rect) =>
                    _controller.Resize((int)rect.Size.X, (int)rect.Size.Y);

                mtkView.Delegate = viewDelegate;

                _controller.Load();

                window.SetContentView(mtkView);
                window.MakeKeyAndOrderFront();
                _app.ActivateIgnoringOtherApps(true);
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

        public void Dispose() => _controller.Dispose();
    }
}
