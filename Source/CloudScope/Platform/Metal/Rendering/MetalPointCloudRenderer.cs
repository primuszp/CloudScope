using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CloudScope.Loading;
using CloudScope.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalPointCloudRenderer : IPointCloudRenderer
    {
        private const int PointStride = 24; // 6 floats
        private const int AttributeStride = 28; // 7 floats
        private const int PointsPerChunk = PointRenderUploadBuilder.DefaultPointsPerChunk;
        private static readonly PointRenderLimits Limits = PointRenderLimits.Load("METAL");

        // Triple-buffered uniforms — CPU never blocks waiting for GPU to finish.
        private const int UniformBufferCount = 3;
        private readonly MTLBuffer[] _uniformBuffers = new MTLBuffer[UniformBufferCount];
        private readonly MTLBuffer[] _attributeUniformBuffers = new MTLBuffer[UniformBufferCount];
        private int _uniformBufferIndex;

        private MTLRenderPipelineState _pipeline;
        private MTLRenderPipelineState _attributePipeline;
        private MTLDepthStencilState _depthState;
        private MTLBuffer[] _pointChunks = Array.Empty<MTLBuffer>();
        private MTLBuffer[] _attributeChunks = Array.Empty<MTLBuffer>();
        private int[] _chunkCounts = Array.Empty<int>();
        private MTLBuffer _classPaletteBuffer;
        private int _pointCount;
        private bool _hasAttributes;
        private bool _hasSourceColors;
        private ColorSource _colorSource = ColorSource.Rgb;
        private PointRenderChunk[] _chunks = Array.Empty<PointRenderChunk>();
        private PointDrawRange[] _drawRanges = Array.Empty<PointDrawRange>();

        // Data uploaded before Initialize() — deferred to first Initialize() call.
        private PointCloudRenderData? _pendingData;

        public int PointCount => _pointCount;
        public bool SupportsAttributeColoring => true;
        public bool CanUpdateColorSourceWithoutUpload => _hasAttributes && _hasSourceColors;

        public void Initialize()
        {
            var device = MetalFrameContext.Device;

            _pipeline = MetalShaderLibrary.CreatePointPipeline(
                device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float);
            _attributePipeline = MetalShaderLibrary.CreateAttributePointPipeline(
                device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float);
            _depthState = MetalShaderLibrary.CreateDepthState(device, depthWrite: true);

            ulong uniformSize = (ulong)Unsafe.SizeOf<MetalPointUniforms>();
            ulong attributeUniformSize = (ulong)Unsafe.SizeOf<MetalAttributePointUniforms>();
            for (int i = 0; i < UniformBufferCount; i++)
            {
                _uniformBuffers[i] = device.NewBuffer(uniformSize, MTLResourceOptions.ResourceStorageModeShared);
                _attributeUniformBuffers[i] = device.NewBuffer(attributeUniformSize, MTLResourceOptions.ResourceStorageModeShared);
            }

            UploadClassPalette(device);

            // Flush any data that arrived before the Metal device was ready.
            if (_pendingData is { } pendingData)
            {
                _pendingData = null;
                Upload(pendingData);
            }
        }

        public void Upload(PointCloudRenderData data)
        {
            int requestedCount = data.Count;
            _pointCount = Math.Min(requestedCount, Limits.MaxResidentPoints);
            _hasAttributes = data.HasAttributes;
            _hasSourceColors = data.HasSourceColors;
            _colorSource = data.ColorSource;
            ReleasePointChunks();
            ReleaseAttributeChunks();

            // Device not ready yet (called before Initialize / app.Run) — defer.
            if (MetalFrameContext.Device.NativePtr == IntPtr.Zero)
            {
                _pendingData = data;
                return;
            }

            if (_pointCount == 0)
                return;

            using PointSpatialUploadLayout layout = PointRenderUploadBuilder.BuildSpatialLayout(data, _pointCount);
            _chunks = layout.Chunks;
            _drawRanges = new PointDrawRange[_chunks.Length];
            UploadToGpu(data, _pointCount, layout.UploadOrder);

            if (_hasAttributes)
                UploadAttributesToGpu(data, _pointCount, layout.UploadOrder);
        }

        public void UpdateColorSource(ColorSource source) => _colorSource = source;

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
                Math.Min(Limits.MaxDrawPointsPerFrame, _pointCount));

            _uniformBufferIndex = (_uniformBufferIndex + 1) % UniformBufferCount;
            var uniformBuffer = _uniformBuffers[_uniformBufferIndex];
            MetalBufferWriter.Write(uniformBuffer, new MetalPointUniforms(view, projection, pointSize));

            var encoder = frame.RenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero) return 0;

            bool useAttributePipeline = _hasAttributes
                && _attributeChunks.Length == _pointChunks.Length
                && _attributePipeline.NativePtr != IntPtr.Zero
                && _classPaletteBuffer.NativePtr != IntPtr.Zero;

            encoder.SetRenderPipelineState(useAttributePipeline ? _attributePipeline : _pipeline);
            encoder.SetDepthStencilState(_depthState);
            if (useAttributePipeline)
            {
                var attributeUniformBuffer = _attributeUniformBuffers[_uniformBufferIndex];
                MetalBufferWriter.Write(
                    attributeUniformBuffer,
                    new MetalAttributePointUniforms(view, projection, pointSize, PointRenderAttributeBuilder.MapColorSource(_colorSource)));
                encoder.SetVertexBuffer(attributeUniformBuffer, 0, 1);
                encoder.SetVertexBuffer(_classPaletteBuffer, 0, 3);
            }
            else
            {
                MetalBufferWriter.Write(uniformBuffer, new MetalPointUniforms(view, projection, pointSize));
                encoder.SetVertexBuffer(uniformBuffer, 0, 1);
            }

            int rangeCount = PointChunkDrawPlanner.FillDrawRanges(
                _chunks, ref view, ref projection, drawCount, _drawRanges, out int drawnPointCount);
            for (int i = 0; i < rangeCount; i++)
            {
                PointDrawRange range = _drawRanges[i];
                int first = range.First;
                int remaining = range.Count;
                while (remaining > 0)
                {
                    int bufferIndex = first / PointsPerChunk;
                    int firstInBuffer = first - bufferIndex * PointsPerChunk;
                    int count = Math.Min(remaining, _chunkCounts[bufferIndex] - firstInBuffer);
                    encoder.SetVertexBuffer(_pointChunks[bufferIndex], 0, 0);
                    if (useAttributePipeline)
                        encoder.SetVertexBuffer(_attributeChunks[bufferIndex], 0, 2);
                    encoder.DrawPrimitives(MTLPrimitiveType.Point, (ulong)firstInBuffer, (ulong)count);
                    first += count;
                    remaining -= count;
                }
            }
            return drawnPointCount;
        }

        public void Dispose()
        {
            ReleasePointChunks();
            ReleaseAttributeChunks();
            for (int i = 0; i < _uniformBuffers.Length; i++)
            {
                if (_uniformBuffers[i].NativePtr != IntPtr.Zero)
                    NativeRelease(_uniformBuffers[i].NativePtr);
                _uniformBuffers[i] = default;

                if (_attributeUniformBuffers[i].NativePtr != IntPtr.Zero)
                    NativeRelease(_attributeUniformBuffers[i].NativePtr);
                _attributeUniformBuffers[i] = default;
            }
            Release(_classPaletteBuffer.NativePtr);
            Release(_pipeline.NativePtr);
            Release(_attributePipeline.NativePtr);
            Release(_depthState.NativePtr);
            _classPaletteBuffer = default;
            _pipeline = default;
            _attributePipeline = default;
            _depthState = default;
            _pendingData = null;
            _chunks = Array.Empty<PointRenderChunk>();
            _drawRanges = Array.Empty<PointDrawRange>();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private unsafe void UploadToGpu(PointCloudRenderData data, int residentCount, int[]? uploadOrder)
        {
            if (residentCount <= 0) return;

            var device = MetalFrameContext.Device;
            int chunkCount = PointRenderUploadBuilder.GetChunkCount(residentCount, PointsPerChunk);
            _pointChunks = new MTLBuffer[chunkCount];
            _chunkCounts = new int[chunkCount];

            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                int pointOffset = chunk * PointsPerChunk;
                int count = PointRenderUploadBuilder.GetChunkPointCount(residentCount, chunk, PointsPerChunk);
                ulong byteSize = (ulong)(count * PointStride);

                var buffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
                var destination = new Span<PointData>(buffer.Contents.ToPointer(), count);
                PointRenderUploadBuilder.FillPoints(data, destination, pointOffset, uploadOrder);
                buffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });

                _pointChunks[chunk] = buffer;
                _chunkCounts[chunk] = count;
            }
        }

        private unsafe void UploadAttributesToGpu(PointCloudRenderData data, int residentCount, int[]? uploadOrder)
        {
            if (residentCount <= 0)
                return;

            var device = MetalFrameContext.Device;
            int chunkCount = PointRenderUploadBuilder.GetChunkCount(residentCount, PointsPerChunk);
            _attributeChunks = new MTLBuffer[chunkCount];

            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                int pointOffset = chunk * PointsPerChunk;
                int count = PointRenderUploadBuilder.GetChunkPointCount(residentCount, chunk, PointsPerChunk);
                ulong byteSize = (ulong)(count * AttributeStride);

                var buffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
                var dst = (PointRenderAttributeData*)buffer.Contents.ToPointer();
                PointRenderAttributeBuilder.Fill(
                    data, new Span<PointRenderAttributeData>(dst, count), pointOffset, uploadOrder);

                buffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });
                _attributeChunks[chunk] = buffer;
            }
        }

        private unsafe void UploadClassPalette(MTLDevice device)
        {
            const int PaletteSize = 256;
            ulong byteSize = (ulong)(PaletteSize * Unsafe.SizeOf<MetalPaletteColor>());
            _classPaletteBuffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            var dst = (MetalPaletteColor*)_classPaletteBuffer.Contents.ToPointer();
            for (int i = 0; i < PaletteSize; i++)
            {
                var color = ClassColorPalette.GetColor((byte)i);
                dst[i] = new MetalPaletteColor(color.X, color.Y, color.Z);
            }

            _classPaletteBuffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });
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

        private void ReleaseAttributeChunks()
        {
            foreach (var chunk in _attributeChunks)
            {
                if (chunk.NativePtr != IntPtr.Zero)
                    NativeRelease(chunk.NativePtr);
            }
            _attributeChunks = Array.Empty<MTLBuffer>();
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
