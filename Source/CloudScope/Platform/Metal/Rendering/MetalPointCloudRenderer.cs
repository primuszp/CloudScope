using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CloudScope.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalPointCloudRenderer : IPointCloudRenderer
    {
        private const int PointStride = 24; // 6 floats

        // Triple-buffered uniforms — CPU never blocks waiting for GPU to finish.
        private const int UniformBufferCount = 3;
        private readonly MTLBuffer[] _uniformBuffers = new MTLBuffer[UniformBufferCount];
        private int _uniformBufferIndex;

        private MTLRenderPipelineState _pipeline;
        private MTLDepthStencilState _depthState;
        private MTLBuffer _pointBuffer;
        private int _pointCount;

        // Points uploaded before Initialize() — deferred to first Initialize() call.
        private PointData[]? _pendingPoints;

        public int PointCount => _pointCount;

        public void Initialize()
        {
            var device = MetalFrameContext.Device;

            _pipeline = MetalShaderLibrary.CreatePointPipeline(
                device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float);
            _depthState = MetalShaderLibrary.CreateDepthState(device, depthWrite: true);

            ulong uniformSize = (ulong)Unsafe.SizeOf<MetalPointUniforms>();
            for (int i = 0; i < UniformBufferCount; i++)
                _uniformBuffers[i] = device.NewBuffer(uniformSize, MTLResourceOptions.ResourceStorageModeShared);

            // Flush any points that arrived before the Metal device was ready.
            if (_pendingPoints != null)
            {
                UploadToGpu(_pendingPoints);
                _pendingPoints = null;
            }
        }

        public void Upload(PointData[] points)
        {
            _pointCount = points.Length;
            _pointBuffer = default;

            // Device not ready yet (called before Initialize / app.Run) — defer.
            if (MetalFrameContext.Device.NativePtr == IntPtr.Zero)
            {
                _pendingPoints = points;
                return;
            }

            UploadToGpu(points);
        }

        public int Render(ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius)
        {
            if (_pointCount <= 0 || _pointBuffer.NativePtr == IntPtr.Zero || _pipeline.NativePtr == IntPtr.Zero)
                return 0;

            var cmdBuffer  = MetalFrameContext.CurrentCommandBuffer;
            var descriptor = MetalFrameContext.CurrentRenderPassDescriptor;
            if (cmdBuffer.NativePtr == IntPtr.Zero || descriptor.NativePtr == IntPtr.Zero)
                return 0;

            float subsampleRatio  = (float)(cloudRadius / Math.Max(halfViewSize, cloudRadius * 0.001));
            float subsampleFactor = Math.Clamp(subsampleRatio * subsampleRatio, 0.005f, 1f);
            int drawCount = Math.Max((int)(_pointCount * subsampleFactor), Math.Min(100_000, _pointCount));

            _uniformBufferIndex = (_uniformBufferIndex + 1) % UniformBufferCount;
            var uniformBuffer = _uniformBuffers[_uniformBufferIndex];

            MetalBufferWriter.Write(uniformBuffer, new MetalPointUniforms(view, projection, pointSize));

            var rpd = descriptor;
            if (MetalFrameContext.FirstEncoderDone)
            {
                var ca2 = rpd.ColorAttachments.Object(0);
                ca2.LoadAction = MTLLoadAction.Load;
                rpd.ColorAttachments.SetObject(ca2, 0);
            }

            var encoder = cmdBuffer.RenderCommandEncoder(rpd);
            MetalFrameContext.MarkFirstEncoderDone();
            encoder.SetRenderPipelineState(_pipeline);
            encoder.SetDepthStencilState(_depthState);
            encoder.SetVertexBuffer(_pointBuffer, 0, 0);
            encoder.SetVertexBuffer(uniformBuffer, 0, 1);
            encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (ulong)drawCount);
            encoder.EndEncoding();

            return drawCount;
        }

        public void Dispose() { }

        // ── Private ───────────────────────────────────────────────────────────────

        private unsafe void UploadToGpu(PointData[] points)
        {
            if (points.Length == 0) return;

            var device = MetalFrameContext.Device;
            ulong byteSize = (ulong)(points.Length * PointStride);

            // Stage into shared memory, then blit to GPU-private VRAM.
            var staging = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            fixed (PointData* src = points)
                Buffer.MemoryCopy(src, staging.Contents.ToPointer(), byteSize, byteSize);
            staging.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });

            var privateBuffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModePrivate);
            if (privateBuffer.NativePtr == IntPtr.Zero)
            {
                _pointBuffer = staging;
                return;
            }

            var blitCmd = MetalFrameContext.CommandQueue.CommandBuffer();
            var blit    = blitCmd.BlitCommandEncoder();
            blit.CopyFromBuffer(staging, 0, privateBuffer, 0, byteSize);
            blit.EndEncoding();
            blitCmd.Commit();
            blitCmd.WaitUntilCompleted();

            _pointBuffer = privateBuffer;
        }
    }
}
