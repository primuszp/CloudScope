using System.Runtime.Versioning;
using CloudScope.Platform.MacOS.ObjC;
using CloudScope.Platform.Metal;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Platform.Metal.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

[SupportedOSPlatform("macos")]
internal static class MetalGpuChecks
{
    public static unsafe void Run()
    {
        ObjectiveC.LinkMetal();
        const int columns = 25, rows = 24, size = 4, width = columns * size, height = rows * size;
        var device = MTLDevice.CreateSystemDefaultDevice();
        if (device.NativePtr == IntPtr.Zero) throw new InvalidOperationException("No Metal device.");
        var queue = device.NewCommandQueue();
        var colorDescriptor = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.BGRA8Unorm, width, height, false);
        colorDescriptor.StorageMode = MTLStorageMode.Shared;
        colorDescriptor.Usage = MTLTextureUsage.RenderTarget;
        var color = device.NewTexture(colorDescriptor);
        var depthDescriptor = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.Depth32Float, width, height, false);
        depthDescriptor.StorageMode = MTLStorageMode.Private;
        depthDescriptor.Usage = MTLTextureUsage.RenderTarget;
        var depth = device.NewTexture(depthDescriptor);
        var context = new MetalRenderContext(device, queue, 1);
        using var renderer = new MetalPrimitiveRenderer(context);
        try
        {
            renderer.EnsureResources();
            context.SetViewportSize(width, height);
            for (int pass = 0; pass < 4; pass++)
            {
                var descriptor = new MTLRenderPassDescriptor();
                var attachment = descriptor.ColorAttachments.Object(0);
                attachment.Texture = color;
                attachment.LoadAction = MTLLoadAction.Clear;
                attachment.StoreAction = MTLStoreAction.Store;
                var depthAttachment = descriptor.DepthAttachment;
                depthAttachment.Texture = depth;
                depthAttachment.LoadAction = MTLLoadAction.Clear;
                depthAttachment.StoreAction = MTLStoreAction.Store;
                depthAttachment.ClearDepth = 1;
                context.SetDepthTexture(depth);
                var command = queue.CommandBuffer();
                context.BeginFrame(descriptor, default, command);
                var encoder = command.RenderCommandEncoder(descriptor);
                context.SetRenderCommandEncoder(encoder);
                renderer.SetFrame(context.CurrentFrame!);
                encoder.SetViewport(new MTLViewport { width = width, height = height, zfar = 1 });
                for (int i = 0; i < columns * rows; i++)
                {
                    float x0 = -1f + 2f * (i % columns) / columns, x1 = x0 + 2f / columns;
                    float y1 = 1f - 2f * (i / columns) / rows, y0 = y1 - 2f / rows;
                    var buffer = renderer.CreateFrameBuffer([x0,y0,0.5f, x1,y0,0.5f, x1,y1,0.5f, x0,y0,0.5f, x1,y1,0.5f, x0,y1,0.5f]);
                    Vector4 tint = ((i + pass) % 3) switch { 0 => new(1,0,0,1), 1 => new(0,1,0,1), _ => new(0,0,1,1) };
                    renderer.Draw(buffer, 6, MTLPrimitiveType.Triangle, Matrix4.Identity, tint, depthTest: true, writeDepth: true);
                }
                encoder.EndEncoding();
                command.Commit();
                context.EndFrame(submitted: true);
                command.WaitUntilCompleted();
                if (command.Status == MTLCommandBufferStatus.Error) throw new InvalidOperationException("GPU submission failed.");
                var bytes = new byte[width * height * 4];
                fixed (byte* output = bytes)
                    color.GetBytes((IntPtr)output, width * 4, new MTLRegion { size = new MTLSize { width = width, height = height, depth = 1 } }, 0);
                for (int i = 0; i < columns * rows; i++)
                {
                    int offset = (((i / columns) * size + size / 2) * width + (i % columns) * size + size / 2) * 4;
                    int channel = ((i + pass) % 3) switch { 0 => 2, 1 => 1, _ => 0 };
                    if (bytes[offset + channel] < 250 || bytes[offset + (channel + 1) % 3] > 5 || bytes[offset + (channel + 2) % 3] > 5)
                        throw new InvalidOperationException($"Draw {i} in frame {pass} lost its geometry or uniforms.");
                }
                float picked = new MetalDepthPicker(context).ReadDepth(width / 2, height / 2);
                // IDepthPicker returns OpenGL window depth: (NDC z + 1) / 2.
                const float expectedWindowDepth = (0.5f + 1f) / 2f;
                if (Math.Abs(picked - expectedWindowDepth) > 1e-5f) throw new InvalidOperationException($"Depth readback differs: {picked}.");
                Console.WriteLine($"  ok  Metal frame {pass + 1}: all 600 independent draws verified by color and depth readback");
                MetalResources.Release(descriptor.NativePtr);
            }
        }
        finally
        {
            context.Dispose();
            MetalResources.Release(color.NativePtr);
            MetalResources.Release(depth.NativePtr);
            MetalResources.Release(queue.NativePtr);
        }
    }
}
