#if MACOS
using System;
using AppKit;
using CloudScope.Rendering;
using CoreGraphics;
using Foundation;
using Metal;
using MetalKit;

namespace CloudScope.Platform.Metal
{
    public sealed class MtkViewerHost : NSWindowController, IViewerHost
    {
        private readonly ViewerController controller;
        private readonly MetalRenderBackend metalBackend;
        private readonly MTKView view;
        private readonly MtkViewDelegate viewDelegate;

        public MtkViewerHost(int width, int height, IRenderBackend renderBackend)
        {
            metalBackend = (MetalRenderBackend)renderBackend;
            controller = new ViewerController(width, height, renderBackend);

            var device = MTLDevice.SystemDefault
                ?? throw new PlatformNotSupportedException("No Metal device is available on this Mac.");

            view = new MTKView(new CGRect(0, 0, width, height), device)
            {
                ColorPixelFormat = MTLPixelFormat.BGRA8Unorm,
                DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
                ClearColor = new MTLClearColor(0.015, 0.018, 0.022, 1.0),
                FramebufferOnly = true,
                Paused = false,
                EnableSetNeedsDisplay = false
            };

            viewDelegate = new MtkViewDelegate(controller, metalBackend);
            view.Delegate = viewDelegate;

            Window = new NSWindow(
                new CGRect(0, 0, width, height),
                NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Titled,
                NSBackingStore.Buffered,
                false)
            {
                Title = "CloudScope - Point Cloud Viewer",
                ContentView = view
            };
            Window.Center();
        }

        public void LoadPointCloud(PointData[] points, float cloudRadius = 50f) =>
            controller.LoadPointCloud(points, cloudRadius);

        public void SetLasFilePath(string path) => controller.SetLasFilePath(path);

        public new void Run()
        {
            NSApplication.Init();
            Window?.MakeKeyAndOrderFront(this);
            controller.Load();
            NSApplication.SharedApplication.Run();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                view.Delegate = null;
                viewDelegate.Dispose();
                controller.Dispose();
            }

            base.Dispose(disposing);
        }

        private sealed class MtkViewDelegate : MTKViewDelegate
        {
            private readonly ViewerController controller;
            private readonly MetalRenderBackend backend;
            private readonly System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            private readonly DummyKeyboard dummyKeyboard = new DummyKeyboard();

            public MtkViewDelegate(ViewerController controller, MetalRenderBackend backend)
            {
                this.controller = controller;
                this.backend = backend;
            }

            public override void DrawableSizeWillChange(MTKView view, CGSize size) =>
                controller.Resize((int)size.Width, (int)size.Height);

            public override void Draw(MTKView view)
            {
                var descriptor = new SharpMetal.Metal.MTLRenderPassDescriptor(view.CurrentRenderPassDescriptor.Handle);
                var drawable = new SharpMetal.QuartzCore.CAMetalDrawable(view.CurrentDrawable.Handle);
                var viewWrapper = new CloudScope.Platform.Metal.ObjC.MTKView(view.Handle);

                var cmdBuffer = MetalFrameContext.CommandQueue.CommandBuffer();
                if (cmdBuffer.NativePtr == IntPtr.Zero)
                    return;

                MetalFrameContext.Begin(viewWrapper, descriptor, drawable, cmdBuffer);
                try
                {
                    float dt = (float)stopwatch.Elapsed.TotalSeconds;
                    stopwatch.Restart();
                    controller.UpdateFrame(dt, dummyKeyboard);

                    controller.RenderFrame(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Render exception: {ex}");
                }
                finally
                {
                }
            }
        }
        
        private class DummyKeyboard : IViewerKeyboard
        {
            public bool IsKeyDown(ViewerKey key) => false;
            public bool IsKeyPressed(ViewerKey key) => false;
        }
    }
}
#endif
