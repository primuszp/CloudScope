using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

// ─────────────────────────────────────────────────────────────────────────────
// Minimal Metal Triangle Test
// Uses the EXACT same ObjC wrappers as CloudScope.
// Draws a hardcoded RGB triangle every frame.
// Goal: confirm that continuous rendering works.
// ─────────────────────────────────────────────────────────────────────────────

namespace MetalTriangleTest
{
    [SupportedOSPlatform("macos")]
    static class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Uniforms
        {
            public float PanX;
            public float PanY;
            public float Zoom;
            public float CircleX;
            public float CircleY;
            public float CircleAlpha;
            public float ViewWidth;
            public float ViewHeight;
        }

        static void Main()
        {
            ObjectiveC.LinkMetal();
            ObjectiveC.LinkCoreGraphics();
            ObjectiveC.LinkAppKit();
            ObjectiveC.LinkMetalKit();

            var app         = new NSApplication();
            var appDelegate = new NSApplicationDelegate();
            app.SetDelegate(appDelegate);

            appDelegate.OnWillFinishLaunching += _ =>
                app.SetActivationPolicy(NSApplicationActivationPolicy.Regular);

            appDelegate.OnDidFinishLaunching += _ => Setup(app);

            Console.WriteLine("[Boot] Starting NSApplication.Run()...");
            app.Run();
        }

        static void Setup(NSApplication app)
        {
            var device = MTLDevice.CreateSystemDefaultDevice();
            if (device.NativePtr == IntPtr.Zero) { Console.WriteLine("ERROR: no Metal device"); return; }
            Console.WriteLine("[Init] Metal device OK");

            var queue = device.NewCommandQueue();
            Console.WriteLine($"[Init] Command queue: {(queue.NativePtr != IntPtr.Zero ? "OK" : "FAIL")}");

            var pipeline = CreateTrianglePipeline(device);
            Console.WriteLine($"[Init] Pipeline: {(pipeline.NativePtr != IntPtr.Zero ? "OK" : "FAIL")}");
            var uniformBuffer = device.NewBuffer(
                (ulong)Unsafe.SizeOf<Uniforms>(),
                MTLResourceOptions.ResourceStorageModeShared);
            Console.WriteLine($"[Init] Uniform buffer: {(uniformBuffer.NativePtr != IntPtr.Zero ? "OK" : "FAIL")}");

            // ── Window ────────────────────────────────────────────────────────
            var rect   = new NSRect(150, 150, 800, 600);
            var window = new NSWindow(rect,
                (ulong)(NSStyleMask.Titled | NSStyleMask.Closable |
                        NSStyleMask.Miniaturizable | NSStyleMask.Resizable));
            window.Title = (NSString)"Metal Triangle Test";

            // ── MTKView ───────────────────────────────────────────────────────
            var mtkView = new MTKEventView(rect, device)
            {
                ColorPixelFormat        = MTLPixelFormat.BGRA8Unorm,
                DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,  // same as CloudScope
                ClearColor              = new MTLClearColor { red = 0.1, green = 0.1, blue = 0.2, alpha = 1.0 },
                FramebufferOnly         = false,
                Paused                  = true,
                EnableSetNeedsDisplay   = true
            };
            window.SetContentView(mtkView.NativePtr);
            Console.WriteLine("[Init] MTKView created");

            // ── Draw delegate ─────────────────────────────────────────────────
            int frameCount = 0;
            var sw = Stopwatch.StartNew();
            var frameTimer = Stopwatch.StartNew();
            var inputQueue = new ConcurrentQueue<Action>();
            float panX = 0f, panY = 0f;
            float zoom = 1f;
            float circleX = 0f, circleY = 0f;
            float circleAlpha = 0f;
            int viewWidth = 800, viewHeight = 600;
            int lastMouseX = 0, lastMouseY = 0;
            bool rightDown = false;
            bool isAnimating = false;
            void RequestFrame() => mtkView.SetNeedsDisplay();

            var viewDelegate = new MTKViewDelegate();
            viewDelegate.OnDraw_ = view =>
            {
                while (inputQueue.TryDequeue(out var action))
                    action();

                float dt = (float)frameTimer.Elapsed.TotalSeconds;
                frameTimer.Restart();
                if (circleAlpha > 0f)
                    circleAlpha = Math.Max(0f, circleAlpha - dt * 1.25f);
                isAnimating = circleAlpha > 0.001f;

                var descriptor = view.CurrentRenderPassDescriptor;
                var drawable   = view.CurrentDrawable;

                if (descriptor.NativePtr == IntPtr.Zero)
                {
                    if (frameCount < 10) Console.WriteLine($"[F{frameCount}] descriptor NULL");
                    frameCount++; return;
                }
                if (drawable.NativePtr == IntPtr.Zero)
                {
                    if (frameCount < 10) Console.WriteLine($"[F{frameCount}] drawable NULL");
                    frameCount++; return;
                }

                var colorTexture = descriptor.ColorAttachments.Object(0).Texture;
                if (colorTexture.NativePtr != IntPtr.Zero)
                {
                    int actualWidth = (int)colorTexture.Width;
                    int actualHeight = (int)colorTexture.Height;
                    if (actualWidth > 0 && actualHeight > 0 &&
                        (actualWidth != viewWidth || actualHeight != viewHeight))
                    {
                        viewWidth = actualWidth;
                        viewHeight = actualHeight;
                        mtkView.UpdateDrawableSize(actualWidth, actualHeight);
                        if (frameCount < 10)
                            Console.WriteLine($"[F{frameCount}] drawable size {actualWidth}x{actualHeight}");
                    }
                }

                var cmdBuf = queue.CommandBuffer();
                if (cmdBuf.NativePtr == IntPtr.Zero)
                {
                    Console.WriteLine($"[F{frameCount}] cmdBuffer NULL");
                    frameCount++; return;
                }

                var encoder = cmdBuf.RenderCommandEncoder(descriptor);
                if (encoder.NativePtr == IntPtr.Zero)
                {
                    Console.WriteLine($"[F{frameCount}] encoder NULL");
                    cmdBuf.Commit(); frameCount++; return;
                }

                if (pipeline.NativePtr != IntPtr.Zero)
                {
                    WriteUniforms(uniformBuffer, new Uniforms
                    {
                        PanX = panX,
                        PanY = panY,
                        Zoom = zoom,
                        CircleX = circleX,
                        CircleY = circleY,
                        CircleAlpha = circleAlpha,
                        ViewWidth = viewWidth,
                        ViewHeight = viewHeight
                    });
                    encoder.SetRenderPipelineState(pipeline);
                    encoder.SetVertexBuffer(uniformBuffer, 0, 0);
                    encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
                    if (circleAlpha > 0.001f && CirclePipeline.NativePtr != IntPtr.Zero)
                    {
                        encoder.SetRenderPipelineState(CirclePipeline);
                        encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, 1);
                    }
                }

                encoder.EndEncoding();
                cmdBuf.PresentDrawable(drawable);
                cmdBuf.Commit();
                cmdBuf.WaitUntilScheduled();

                if (frameCount < 10 || frameCount % 120 == 0)
                    Console.WriteLine($"[F{frameCount}] rendered OK  t={sw.Elapsed.TotalSeconds:F1}s");
                frameCount++;
                if (isAnimating)
                    RequestFrame();
            };

            mtkView.Delegate = viewDelegate;
            Console.WriteLine("[Init] Delegate set");

            viewDelegate.OnSizeChange_ = (_, size) =>
            {
                int w = (int)size.Width;
                int h = (int)size.Height;
                if (w <= 0 || h <= 0) return;
                inputQueue.Enqueue(() =>
                {
                    viewWidth = w;
                    viewHeight = h;
                    mtkView.UpdateDrawableSize(w, h);
                    Console.WriteLine($"[Input] resize {w}x{h}");
                    RequestFrame();
                });
            };

            mtkView.OnMouseDown_ = (button, x, y) => inputQueue.Enqueue(() =>
            {
                lastMouseX = x;
                lastMouseY = y;
                if (button == CloudScope.ViewerMouseButton.Right)
                    rightDown = true;
                if (button == CloudScope.ViewerMouseButton.Left)
                {
                    circleX = PixelToNdcX(x, viewWidth);
                    circleY = PixelToNdcY(y, viewHeight);
                    circleAlpha = 1f;
                    Console.WriteLine($"[Input] left click circle ({circleX:F2}, {circleY:F2})");
                }
                RequestFrame();
            });
            mtkView.OnMouseDown_ += (_, _, _) => RequestFrame();

            mtkView.OnMouseUp_ = (button, x, y) => inputQueue.Enqueue(() =>
            {
                lastMouseX = x;
                lastMouseY = y;
                if (button == CloudScope.ViewerMouseButton.Right)
                    rightDown = false;
                RequestFrame();
            });
            mtkView.OnMouseUp_ += (_, _, _) => RequestFrame();

            mtkView.OnMouseMove_ = (x, y) => inputQueue.Enqueue(() =>
            {
                if (rightDown)
                {
                    int dx = x - lastMouseX;
                    int dy = y - lastMouseY;
                    panX += 2f * dx / Math.Max(viewWidth, 1);
                    panY -= 2f * dy / Math.Max(viewHeight, 1);
                    RequestFrame();
                }
                lastMouseX = x;
                lastMouseY = y;
            });
            mtkView.OnMouseMove_ += (_, _) => RequestFrame();

            mtkView.OnMouseWheel_ = (x, y, delta) => inputQueue.Enqueue(() =>
            {
                float before = zoom;
                float factor = delta > 0f ? 1.12f : 1f / 1.12f;
                zoom = Math.Clamp(zoom * factor, 0.15f, 8f);
                Console.WriteLine($"[Input] wheel zoom {before:F2} -> {zoom:F2}");
                RequestFrame();
            });
            mtkView.OnMouseWheel_ += (_, _, _) => RequestFrame();

            window.MakeKeyAndOrderFront();
            app.ActivateIgnoringOtherApps(true);
            mtkView.MakeFirstResponder();
            mtkView.SetNeedsDisplay();
            Console.WriteLine("[Init] Window shown — render loop active");
        }

        // ── MSL shader + pipeline ─────────────────────────────────────────────
        static MTLRenderPipelineState CreateTrianglePipeline(MTLDevice device)
        {
            const string src = @"
#include <metal_stdlib>
using namespace metal;
struct Uniforms {
    float panX;
    float panY;
    float zoom;
    float circleX;
    float circleY;
    float circleAlpha;
    float viewWidth;
    float viewHeight;
};
struct Vert { float4 pos [[position]]; float4 col; float pointSize [[point_size]]; };
vertex Vert vert(uint vid [[vertex_id]], constant Uniforms& u [[buffer(0)]]) {
    float2 p[3] = { {0,0.7}, {-0.7,-0.7}, {0.7,-0.7} };
    float3 c[3] = { {1,0,0}, {0,1,0}, {0,0,1} };
    float2 transformed = p[vid] * u.zoom + float2(u.panX, u.panY);
    return { float4(transformed,0,1), float4(c[vid],1), 1.0 };
}
vertex Vert circle_vert(uint vid [[vertex_id]], constant Uniforms& u [[buffer(0)]]) {
    return { float4(u.circleX, u.circleY, 0, 1), float4(1, 0.92, 0.05, u.circleAlpha), 44.0 };
}
fragment float4 frag(Vert in [[stage_in]]) { return in.col; }
fragment float4 circle_frag(Vert in [[stage_in]], float2 pc [[point_coord]]) {
    float2 d = pc * 2.0 - 1.0;
    float r2 = dot(d, d);
    if (r2 > 1.0) discard_fragment();
    float edge = smoothstep(1.0, 0.72, sqrt(r2));
    return float4(1.0, 0.92, 0.05, in.col.a * edge);
}
";
            var opts   = new MTLCompileOptions();
            var libErr = new NSError(IntPtr.Zero);
            var lib    = device.NewLibrary((NSString)src, opts, ref libErr);
            if (lib.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine($"[Shader] compile error: {(libErr.NativePtr != IntPtr.Zero ? libErr.LocalizedDescription.ToString() : "?")}");
                return default;
            }

            var vertFn = lib.NewFunction((NSString)"vert");
            var fragFn = lib.NewFunction((NSString)"frag");
            var circleVertFn = lib.NewFunction((NSString)"circle_vert");
            var circleFragFn = lib.NewFunction((NSString)"circle_frag");

            var desc = new MTLRenderPipelineDescriptor();
            desc.VertexFunction   = vertFn;
            desc.FragmentFunction = fragFn;

            var ca = desc.ColorAttachments.Object(0);
            ca.PixelFormat = MTLPixelFormat.BGRA8Unorm;
            desc.ColorAttachments.SetObject(ca, 0);

            var pipeErr = new NSError(IntPtr.Zero);
            var state   = device.NewRenderPipelineState(desc, ref pipeErr);
            if (pipeErr.NativePtr != IntPtr.Zero)
                Console.WriteLine($"[Pipeline] error: {pipeErr.LocalizedDescription}");

            var circleDesc = new MTLRenderPipelineDescriptor();
            circleDesc.VertexFunction = circleVertFn;
            circleDesc.FragmentFunction = circleFragFn;
            var circleCa = circleDesc.ColorAttachments.Object(0);
            circleCa.PixelFormat = MTLPixelFormat.BGRA8Unorm;
            circleCa.IsBlendingEnabled = true;
            circleCa.SourceRGBBlendFactor = MTLBlendFactor.SourceAlpha;
            circleCa.DestinationRGBBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;
            circleCa.SourceAlphaBlendFactor = MTLBlendFactor.One;
            circleCa.DestinationAlphaBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;
            circleDesc.ColorAttachments.SetObject(circleCa, 0);

            CirclePipeline = device.NewRenderPipelineState(circleDesc, ref pipeErr);
            if (pipeErr.NativePtr != IntPtr.Zero)
                Console.WriteLine($"[CirclePipeline] error: {pipeErr.LocalizedDescription}");
            return state;
        }

        private static MTLRenderPipelineState CirclePipeline;

        private static unsafe void WriteUniforms(MTLBuffer buffer, Uniforms uniforms)
            => *(Uniforms*)buffer.Contents.ToPointer() = uniforms;

        private static float PixelToNdcX(int x, int width) => x / (float)Math.Max(width, 1) * 2f - 1f;

        private static float PixelToNdcY(int y, int height) => 1f - y / (float)Math.Max(height, 1) * 2f;
    }
}
