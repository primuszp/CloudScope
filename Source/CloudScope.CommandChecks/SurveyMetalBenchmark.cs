using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using CloudScope.IO;
using CloudScope.Loading;
using CloudScope.Platform.MacOS.ObjC;
using CloudScope.Platform.Metal;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using CloudScope.Store;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

/// <summary>Actual offscreen Metal submission latency; this does not measure native input or presentation latency.</summary>
[SupportedOSPlatform("macos")]
internal static class SurveyMetalBenchmark
{
    public static unsafe void Run(string input, string output)
    {
        const int width = 1920, height = 1080, frames = 360, drawBudget = 2_000_000, residentBudget = 6_000_000;
        using var rootPool = new MetalTestAutoreleasePool();
        ObjectiveC.LinkMetal();
        using var store = PointTileStore.Open(input);
        var device = MTLDevice.CreateSystemDefaultDevice();
        if (device.NativePtr == IntPtr.Zero) throw new InvalidOperationException("No Metal device.");
        var queue = device.NewCommandQueue();
        var colorDescriptor = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.BGRA8Unorm, width, height, false);
        colorDescriptor.StorageMode = MTLStorageMode.Shared; colorDescriptor.Usage = MTLTextureUsage.RenderTarget;
        var color = device.NewTexture(colorDescriptor);
        var depthDescriptor = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.Depth32Float, width, height, false);
        depthDescriptor.StorageMode = MTLStorageMode.Private; depthDescriptor.Usage = MTLTextureUsage.RenderTarget;
        var depth = device.NewTexture(depthDescriptor);
        var context = new MetalRenderContext(device, queue, 1);
        var renderer = new MetalStreamingPointCloudRenderer(context);
        var oldFrameBudget = PointRenderBudget.MaxDrawPointsPerFrame; var oldResidentBudget = PointRenderBudget.MaxResidentPoints;
        PointRenderBudget.MaxDrawPointsPerFrame = drawBudget; PointRenderBudget.MaxResidentPoints = residentBudget;
        try
        {
            renderer.Initialize(); renderer.Open([new PointTileLayer("Survey", store)]);
            context.SetViewportSize(width, height); context.SetDepthTexture(depth);
            var camera = SurveyBenchmark.Camera(store);
            long maxDrawn = 0; ulong peakMetalBytes = 0;
            double Draw()
            {
                using var pool = new MetalTestAutoreleasePool();
                var timer = Stopwatch.StartNew();
                var descriptor = new MTLRenderPassDescriptor();
                var attachment = descriptor.ColorAttachments.Object(0); attachment.Texture = color;
                attachment.LoadAction = MTLLoadAction.Clear; attachment.StoreAction = MTLStoreAction.Store;
                attachment.ClearColor = new MTLClearColor { red = .05, green = .05, blue = .05, alpha = 1 };
                var da = descriptor.DepthAttachment; da.Texture = depth;
                da.LoadAction = MTLLoadAction.Clear; da.StoreAction = MTLStoreAction.Store; da.ClearDepth = 1;
                try
                {
                    var command = queue.CommandBuffer(); context.BeginFrame(descriptor, default, command);
                    var encoder = command.RenderCommandEncoder(descriptor); context.SetRenderCommandEncoder(encoder);
                    encoder.SetViewport(new MTLViewport { width = width, height = height, zfar = 1 });
                    renderer.BeginFrame(context.CurrentFrame!);
                    renderer.Render(context.CurrentFrame!, new PointRenderView(camera.GetViewMatrix(), camera.GetProjectionMatrix(), width, height, 2));
                    encoder.EndEncoding(); command.Commit(); context.EndFrame(submitted: true); command.WaitUntilCompleted();
                    if (command.Status == MTLCommandBufferStatus.Error) throw new InvalidOperationException("Metal submission failed.");
                    if (renderer.LoadError != null) throw new IOException(renderer.LoadError);
                    maxDrawn = Math.Max(maxDrawn, renderer.DrawnPointCount); peakMetalBytes = Math.Max(peakMetalBytes, device.CurrentAllocatedSize);
                    return timer.Elapsed.TotalMilliseconds;
                }
                finally { MetalResources.Release(descriptor.NativePtr); }
            }
            var warm = Stopwatch.StartNew();
            double nextLog = 0;
            do
            {
                Draw(); Thread.Sleep(1);
                if (warm.Elapsed.TotalSeconds >= nextLog)
                {
                    var planned = new PointTileVisit[store.Nodes.Length];
                    var planningLayers = new[] { new PointTileLayer("Plan", store) };
                    var view = new PointRenderView(camera.GetViewMatrix(), camera.GetProjectionMatrix(), width, height, 2);
                    int count = PointTileTraversal.Collect(planningLayers, view, drawBudget, planned, new PointTileTraversalScratch(), out long points);
                    int pages = planned.Take(count).Sum(v => (store.Nodes[v.NodeIndex].PointCount + 65535) / 65536);
                    Console.Error.WriteLine($"Warmup {warm.Elapsed.TotalSeconds:F1}s: drawn={renderer.DrawnPointCount}, resident cells={renderer.ResidentCellCount}, pending={renderer.PendingCellCount}, unconstrained visible cells={count}, points={points}, required 64K pages={pages}");
                    nextLog = warm.Elapsed.TotalSeconds + 5;
                }
                if (warm.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("The survey's initial visible cells did not finish loading.");
            } while (renderer.DrawnPointCount == 0 || renderer.PendingCellCount > 0);
            var times = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                camera.OrbitBy(1, 0);
                renderer.UpdateColorSource((ColorSource)((i / 72) % 5));
                times[i] = Draw();
            }
            renderer.UpdateColorSource(ColorSource.Class);
            for (int i = 0; i < 5; i++) Draw();
            var bytes = new byte[width * height * 4];
            fixed (byte* pointer = bytes) color.GetBytes((IntPtr)pointer, width * 4, new MTLRegion { size = new MTLSize { width = width, height = height, depth = 1 } }, 0);
            int nonBackground = 0;
            for (int i = 0; i < bytes.Length; i += 4)
                if (Math.Abs(bytes[i] - 13) > 3 || Math.Abs(bytes[i + 1] - 13) > 3 || Math.Abs(bytes[i + 2] - 13) > 3) nonBackground++;
            if (nonBackground == 0 || maxDrawn == 0 || maxDrawn > drawBudget) throw new InvalidDataException("The GPU benchmark produced no point image or exceeded its draw budget.");
            Array.Sort(times);
            var report = new
            {
                backend = "Metal",
                mode = "Offscreen sequential submit-and-wait latency, not desktop FPS",
                width,
                height,
                frames,
                sourcePoints = store.Header.PointCount,
                drawBudget,
                residentBudget,
                maxDrawn,
                peakMetalBytes,
                nonBackgroundPixels = nonBackground,
                medianMilliseconds = times[frames / 2],
                p95Milliseconds = times[(int)(frames * .95)],
                worstMilliseconds = times[^1],
                meanMilliseconds = times.Average()
            };
            Directory.CreateDirectory(output);
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            AtomicFile.WriteText(Path.Combine(output, "survey-metal.json"), json); Console.WriteLine(json);
        }
        finally
        {
            renderer.Dispose(); context.Dispose();
            PointRenderBudget.MaxDrawPointsPerFrame = oldFrameBudget; PointRenderBudget.MaxResidentPoints = oldResidentBudget;
            MetalResources.Release(color.NativePtr); MetalResources.Release(depth.NativePtr); MetalResources.Release(queue.NativePtr);
        }
    }

}
