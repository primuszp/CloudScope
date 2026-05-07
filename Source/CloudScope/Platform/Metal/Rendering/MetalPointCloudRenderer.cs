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
        private const int PointsPerChunk = 1_000_000;
        private static readonly int MaxDrawPointsPerFrame =
            int.TryParse(Environment.GetEnvironmentVariable("CLOUDSCOPE_METAL_MAX_DRAW_POINTS"), out int maxDrawPoints) && maxDrawPoints > 0
                ? maxDrawPoints
                : 1_000_000;
        private static readonly int MaxResidentPoints =
            int.TryParse(Environment.GetEnvironmentVariable("CLOUDSCOPE_METAL_MAX_RESIDENT_POINTS"), out int maxResidentPoints) && maxResidentPoints > 0
                ? maxResidentPoints
                : 1_000_000;

        // Triple-buffered uniforms — CPU never blocks waiting for GPU to finish.
        private const int UniformBufferCount = 3;
        private readonly MTLBuffer[] _uniformBuffers = new MTLBuffer[UniformBufferCount];
        private int _uniformBufferIndex;

        private MTLRenderPipelineState _pipeline;
        private MTLDepthStencilState _depthState;
        private MTLBuffer[] _pointChunks = Array.Empty<MTLBuffer>();
        private int[] _chunkCounts = Array.Empty<int>();
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
                _pointCount = Math.Min(_pendingPoints.Length, MaxResidentPoints);
                UploadToGpu(_pendingPoints, _pointCount);
                _pendingPoints = null;
            }
        }

        public void Upload(PointData[] points)
        {
            _pointCount = Math.Min(points.Length, MaxResidentPoints);
            ReleasePointChunks();

            // Device not ready yet (called before Initialize / app.Run) — defer.
            if (MetalFrameContext.Device.NativePtr == IntPtr.Zero)
            {
                _pendingPoints = points;
                return;
            }

            UploadToGpu(points, _pointCount);
        }

        private int _renderCallCount;

        public int Render(ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius)
        {
            _renderCallCount++;

            if (_pointCount <= 0 || _pointChunks.Length == 0 || _pipeline.NativePtr == IntPtr.Zero)
                return 0;

            if (float.IsNaN(view.M11) || float.IsNaN(projection.M11))
                return 0;

            var cmdBuffer  = MetalFrameContext.CurrentCommandBuffer;
            var descriptor = MetalFrameContext.CurrentRenderPassDescriptor;
            if (cmdBuffer.NativePtr == IntPtr.Zero || descriptor.NativePtr == IntPtr.Zero)
                return 0;

            float subsampleRatio  = (float)(cloudRadius / Math.Max(halfViewSize, cloudRadius * 0.001));
            float subsampleFactor = Math.Clamp(subsampleRatio * subsampleRatio, 0.005f, 1f);
            int drawCount = Math.Max((int)(_pointCount * subsampleFactor), Math.Min(100_000, _pointCount));
            drawCount = Math.Min(drawCount, Math.Min(MaxDrawPointsPerFrame, _pointCount));

            _uniformBufferIndex = (_uniformBufferIndex + 1) % UniformBufferCount;
            var uniformBuffer = _uniformBuffers[_uniformBufferIndex];
            MetalBufferWriter.Write(uniformBuffer, new MetalPointUniforms(view, projection, pointSize));

            var encoder = MetalFrameContext.CurrentRenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero) return 0;

            encoder.SetRenderPipelineState(_pipeline);
            encoder.SetDepthStencilState(_depthState);
            encoder.SetVertexBuffer(uniformBuffer, 0, 1);
            int remaining = drawCount;
            for (int i = 0; i < _pointChunks.Length && remaining > 0; i++)
            {
                int chunkDrawCount = Math.Min(_chunkCounts[i], remaining);
                if (chunkDrawCount <= 0)
                    break;

                encoder.SetVertexBuffer(_pointChunks[i], 0, 0);
                encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (ulong)chunkDrawCount);
                remaining -= chunkDrawCount;
            }
            return drawCount;
        }

        public void Dispose()
        {
            ReleasePointChunks();
            for (int i = 0; i < _uniformBuffers.Length; i++)
            {
                if (_uniformBuffers[i].NativePtr != IntPtr.Zero)
                    NativeRelease(_uniformBuffers[i].NativePtr);
                _uniformBuffers[i] = default;
            }
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private unsafe void UploadToGpu(PointData[] points, int residentCount)
        {
            if (residentCount <= 0) return;

            var device = MetalFrameContext.Device;
            int chunkCount = (residentCount + PointsPerChunk - 1) / PointsPerChunk;
            _pointChunks = new MTLBuffer[chunkCount];
            _chunkCounts = new int[chunkCount];

            fixed (PointData* srcBase = points)
            {
                for (int chunk = 0; chunk < chunkCount; chunk++)
                {
                    int pointOffset = chunk * PointsPerChunk;
                    int count = Math.Min(PointsPerChunk, residentCount - pointOffset);
                    ulong byteSize = (ulong)(count * PointStride);

                    var buffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
                    Buffer.MemoryCopy(srcBase + pointOffset, buffer.Contents.ToPointer(), byteSize, byteSize);
                    buffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });

                    _pointChunks[chunk] = buffer;
                    _chunkCounts[chunk] = count;
                }
            }
        }

        private void ReleasePointChunks()
        {
            foreach (var chunk in _pointChunks)
            {
                if (chunk.NativePtr != IntPtr.Zero)
                    NativeRelease(chunk.NativePtr);
            }
            _pointChunks = Array.Empty<MTLBuffer>();
            _chunkCounts = Array.Empty<int>();
        }

        [System.Runtime.InteropServices.DllImport("libobjc.dylib", EntryPoint = "objc_release")]
        private static extern void NativeRelease(IntPtr obj);
    }
}
