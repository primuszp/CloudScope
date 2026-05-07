using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class MetalRenderBackend : IRenderBackend
    {
        public RenderBackendKind Kind => RenderBackendKind.Metal;

        // ── Diagnostic triangle ─────────────────────────────────────────────────────
        // Draws a solid red triangle via the same encoder CloudScope uses.
        // If the triangle appears → encoder works, bug is in the renderers.
        // If NOT → encoder itself broken (descriptor/format issue).
        private MTLRenderPipelineState _diagPipeline;
        private int _frameCount;

        public IPointCloudRenderer  CreatePointCloudRenderer()  => new MetalPointCloudRenderer();
        public IHighlightRenderer   CreateHighlightRenderer()   => new MetalHighlightRenderer();
        public IOverlayRenderer     CreateOverlayRenderer()     => new MetalOverlayRenderer();
        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => MetalRendererFactory.CreateSelectionGizmoRenderers();
        public IDepthPicker CreateDepthPicker() => new MetalDepthPicker();

        public void InitializeFrameState()
        {
            // Create a minimal solid-colour pipeline for the diagnostic triangle.
            const string src = @"
#include <metal_stdlib>
using namespace metal;
struct V { float4 pos [[position]]; };
vertex V diag_vert(uint id [[vertex_id]]) {
    float2 p[3] = { {0, 0.6}, {-0.6,-0.6}, {0.6,-0.6} };
    return { float4(p[id], 0, 1) };
}
fragment float4 diag_frag(V in [[stage_in]]) { return float4(1,0,0,1); }
";
            var libErr = new NSError(IntPtr.Zero);
            var lib = MetalFrameContext.Device.NewLibrary(
                (NSString)src, new MTLCompileOptions(IntPtr.Zero), ref libErr);
            if (lib.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine($"[Diag] shader compile failed: {libErr.LocalizedDescription}");
                return;
            }

            var desc = new MTLRenderPipelineDescriptor();
            desc.VertexFunction   = lib.NewFunction((NSString)"diag_vert");
            desc.FragmentFunction = lib.NewFunction((NSString)"diag_frag");
            desc.DepthAttachmentPixelFormat = MTLPixelFormat.Depth32Float;

            var ca = desc.ColorAttachments.Object(0);
            ca.PixelFormat = MTLPixelFormat.BGRA8Unorm;
            desc.ColorAttachments.SetObject(ca, 0);

            var pipeErr = new NSError(IntPtr.Zero);
            _diagPipeline = MetalFrameContext.Device.NewRenderPipelineState(desc, ref pipeErr);
            if (pipeErr.NativePtr != IntPtr.Zero)
                Console.WriteLine($"[Diag] pipeline failed: {pipeErr.LocalizedDescription}");
            else
                Console.WriteLine("[Diag] diagnostic triangle pipeline OK");
        }

        public void BeginFrame()
        {
            var descriptor = MetalFrameContext.CurrentRenderPassDescriptor;
            if (descriptor.NativePtr == IntPtr.Zero)
            {
                if (_frameCount < 5) Console.WriteLine($"[BeginFrame {_frameCount}] descriptor NULL");
                _frameCount++;
                return;
            }

            var da = descriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero && da.Texture.NativePtr != IntPtr.Zero)
                MetalFrameContext.SetDepthTexture(da.Texture);

            var cmdBuffer = MetalFrameContext.CurrentCommandBuffer;
            if (cmdBuffer.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine($"[BeginFrame {_frameCount}] cmdBuffer NULL");
                _frameCount++;
                return;
            }

            var encoder = cmdBuffer.RenderCommandEncoder(descriptor);
            if (encoder.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine($"[BeginFrame {_frameCount}] encoder NULL");
                _frameCount++;
                return;
            }

            if (_frameCount < 5) Console.WriteLine($"[BeginFrame {_frameCount}] encoder OK");

            MetalFrameContext.CurrentRenderCommandEncoder = encoder;

            // ── Diagnostic: draw a red triangle via this encoder ──────────────────
            // COMMENT OUT these 3 lines once the bug is found.
            if (_diagPipeline.NativePtr != IntPtr.Zero)
            {
                encoder.SetRenderPipelineState(_diagPipeline);
                encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
            }

            _frameCount++;
        }

        public void Resize(int width, int height) { }
    }
}
