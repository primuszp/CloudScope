using System;
using System.Diagnostics;
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
                Paused                  = false,
                EnableSetNeedsDisplay   = false
            };
            window.SetContentView(mtkView.NativePtr);
            Console.WriteLine("[Init] MTKView created");

            // ── Draw delegate ─────────────────────────────────────────────────
            int frameCount = 0;
            var sw = Stopwatch.StartNew();

            var viewDelegate = new MTKViewDelegate();
            viewDelegate.OnDraw_ = view =>
            {
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
                    encoder.SetRenderPipelineState(pipeline);
                    encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
                }

                encoder.EndEncoding();
                cmdBuf.PresentDrawable(drawable);
                cmdBuf.Commit();

                if (frameCount < 10 || frameCount % 120 == 0)
                    Console.WriteLine($"[F{frameCount}] rendered OK  t={sw.Elapsed.TotalSeconds:F1}s");
                frameCount++;
            };

            mtkView.Delegate = viewDelegate;
            Console.WriteLine("[Init] Delegate set");

            window.MakeKeyAndOrderFront();
            app.ActivateIgnoringOtherApps(true);
            mtkView.MakeFirstResponder();
            Console.WriteLine("[Init] Window shown — render loop active");
        }

        // ── MSL shader + pipeline ─────────────────────────────────────────────
        static MTLRenderPipelineState CreateTrianglePipeline(MTLDevice device)
        {
            const string src = @"
#include <metal_stdlib>
using namespace metal;
struct Vert { float4 pos [[position]]; float4 col; };
vertex Vert vert(uint vid [[vertex_id]]) {
    float2 p[3] = { {0,0.7}, {-0.7,-0.7}, {0.7,-0.7} };
    float3 c[3] = { {1,0,0}, {0,1,0}, {0,0,1} };
    return { float4(p[vid],0,1), float4(c[vid],1) };
}
fragment float4 frag(Vert in [[stage_in]]) { return in.col; }
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
            return state;
        }
    }
}
