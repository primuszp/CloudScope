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
        private const int DefaultMaxResidentPoints = 5_000_000;
        private const int DefaultMaxDrawPointsPerFrame = 5_000_000;
        private static readonly int MaxDrawPointsPerFrame =
            int.TryParse(Environment.GetEnvironmentVariable("CLOUDSCOPE_METAL_MAX_DRAW_POINTS"), out int maxDrawPoints) && maxDrawPoints > 0
                ? maxDrawPoints
                : DefaultMaxDrawPointsPerFrame;
        private static readonly int MaxResidentPoints =
            int.TryParse(Environment.GetEnvironmentVariable("CLOUDSCOPE_METAL_MAX_RESIDENT_POINTS"), out int maxResidentPoints) && maxResidentPoints > 0
                ? maxResidentPoints
                : DefaultMaxResidentPoints;

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
        public bool CanUpdateColorSourceWithoutUpload => false;

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

        public void Upload(PointCloudRenderData data)
        {
            PointData[] points = data.Points;
            int[]? renderOrder = data.RenderOrder;
            int requestedCount = data.Count;
            _pointCount = Math.Min(requestedCount, MaxResidentPoints);
            ReleasePointChunks();

            // Device not ready yet (called before Initialize / app.Run) — defer.
            if (MetalFrameContext.Device.NativePtr == IntPtr.Zero)
            {
                _pendingPoints = renderOrder is { Length: > 0 }
                    ? BuildOrderedPointBuffer(points, renderOrder, _pointCount)
                    : points;
                return;
            }

            if (renderOrder is { Length: > 0 })
                UploadToGpu(points, _pointCount, renderOrder);
            else
                UploadToGpu(points, _pointCount);
        }

        public void UpdateColorSource(CloudScope.Loading.ColorSource source)
        {
        }

        public int Render(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius)
        {
            if (_pointCount <= 0 || _pointChunks.Length == 0 || _pipeline.NativePtr == IntPtr.Zero)
                return 0;

            if (float.IsNaN(view.M11) || float.IsNaN(projection.M11))
                return 0;

            if (frameData is not MetalFrameState frame
                || frame.CommandBuffer.NativePtr == IntPtr.Zero
                || frame.RenderPassDescriptor.NativePtr == IntPtr.Zero)
                return 0;

            int drawCount = PointDrawBudget.Compute(
                _pointCount,
                halfViewSize,
                cloudRadius,
                Math.Min(MaxDrawPointsPerFrame, _pointCount));

            _uniformBufferIndex = (_uniformBufferIndex + 1) % UniformBufferCount;
            var uniformBuffer = _uniformBuffers[_uniformBufferIndex];
            MetalBufferWriter.Write(uniformBuffer, new MetalPointUniforms(view, projection, pointSize));

            var encoder = frame.RenderCommandEncoder;
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
            Release(_pipeline.NativePtr);
            Release(_depthState.NativePtr);
            _pipeline = default;
            _depthState = default;
            _pendingPoints = null;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private unsafe void UploadToGpu(PointData[] points, int residentCount, int[]? renderOrder = null)
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
                    if (renderOrder is null)
                    {
                        Buffer.MemoryCopy(srcBase + pointOffset, buffer.Contents.ToPointer(), byteSize, byteSize);
                    }
                    else
                    {
                        var dst = (PointData*)buffer.Contents.ToPointer();
                        for (int i = 0; i < count; i++)
                            dst[i] = srcBase[renderOrder[pointOffset + i]];
                    }
                    buffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });

                    _pointChunks[chunk] = buffer;
                    _chunkCounts[chunk] = count;
                }
            }
        }

        private static PointData[] BuildOrderedPointBuffer(PointData[] points, int[] renderOrder, int count)
        {
            var ordered = new PointData[count];
            for (int i = 0; i < count; i++)
                ordered[i] = points[renderOrder[i]];
            return ordered;
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

        private static void Release(IntPtr nativePtr)
        {
            if (nativePtr != IntPtr.Zero)
                NativeRelease(nativePtr);
        }
    }
}
