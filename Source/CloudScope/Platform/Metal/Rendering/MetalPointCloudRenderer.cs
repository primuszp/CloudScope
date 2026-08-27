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

        private readonly MetalRenderContext _context;

        public MetalPointCloudRenderer(MetalRenderContext context) => _context = context;
        private const int PointStride = GpuPointVertex.Stride;
        private const int AttributeStride = GpuPointAttribute.Stride;
        private const int PointsPerChunk = PointRenderUploadBuilder.DefaultPointsPerChunk;
        private static PointRenderLimits Limits => PointRenderLimits.For("METAL");

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
        private PointOctreeNode[] _nodes = Array.Empty<PointOctreeNode>();
        private PointDrawRange[] _drawRanges = Array.Empty<PointDrawRange>();
        private PointLodScratch _lodScratch = new(0);

        // Data uploaded before Initialize() — deferred to first Initialize() call.
        private PointCloudRenderData? _pendingData;

        public int PointCount => _pointCount;
        public bool SupportsAttributeColoring => true;
        public bool CanUpdateColorSourceWithoutUpload => _hasAttributes && _hasSourceColors;

        public void Initialize()
        {
            var device = _context.Device;

            _pipeline = MetalShaderLibrary.CreatePackedPointPipeline(
                device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float, _context.SampleCount);
            _attributePipeline = MetalShaderLibrary.CreateAttributePointPipeline(
                device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float, _context.SampleCount);
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
            _pointCount = Math.Min(requestedCount, ResolveResidentLimit(data.HasAttributes));
            _hasAttributes = data.HasAttributes;
            _hasSourceColors = data.HasSourceColors;
            _colorSource = data.ColorSource;
            ReleasePointChunks();
            ReleaseAttributeChunks();

            // Device not ready yet (called before Initialize / app.Run) — defer.
            if (_context.Device.NativePtr == IntPtr.Zero)
            {
                _pendingData = data;
                return;
            }

            if (_pointCount == 0)
                return;

            using PointCloudOctreeLayout layout = PointCloudOctree.Build(data, _pointCount);
            _nodes = layout.Nodes;
            _drawRanges = new PointDrawRange[_nodes.Length];
            _lodScratch = new PointLodScratch(_nodes.Length);
            UploadToGpu(data, _pointCount, layout.UploadOrder);

            if (_hasAttributes)
                UploadAttributesToGpu(data, _pointCount, layout.UploadOrder);
        }

        public void UpdateColorSource(ColorSource source) => _colorSource = source;

        /// <summary>
        /// How many points may stay in GPU memory. Metal reports what the device wants to keep
        /// resident, so a cloud too large for the hardware is trimmed rather than left to fail
        /// the allocation.
        /// </summary>
        private int ResolveResidentLimit(bool hasAttributes)
        {
            int bytesPerPoint = PointStride + (hasAttributes ? AttributeStride : 0);
            long? working = _context.Device.NativePtr == IntPtr.Zero
                ? null
                : (long)_context.Device.RecommendedMaxWorkingSetSize;
            return Limits.ResolveResidentLimit(working, bytesPerPoint);
        }

        public int Render(IRenderFrameData frameData, in PointRenderView renderView)
        {
            if (_pointCount <= 0 || _pointChunks.Length == 0 || _pipeline.NativePtr == IntPtr.Zero)
                return 0;

            Matrix4 view = renderView.View;
            Matrix4 projection = renderView.Projection;
            if (float.IsNaN(view.M11) || float.IsNaN(projection.M11))
                return 0;

            if (frameData is not MetalFrameState frame
                || frame.CommandBuffer.NativePtr == IntPtr.Zero
                || frame.RenderPassDescriptor.NativePtr == IntPtr.Zero)
                return 0;

            int drawBudget = Limits.GetFrameBudget(_pointCount);

            int rangeCount = PointLodPlanner.Plan(
                _nodes, in renderView, drawBudget, _drawRanges, _lodScratch, out int drawnPointCount);
            if (rangeCount == 0)
                return 0;

            float pointSize = renderView.PointSize;
            _uniformBufferIndex = (_uniformBufferIndex + 1) % UniformBufferCount;
            var uniformBuffer = _uniformBuffers[_uniformBufferIndex];

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

            // Ranges arrive nearest-first, so consecutive ones often land in the same buffer;
            // rebinding only on a change saves a couple of thousand encoder calls per frame.
            int boundChunk = -1;
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
                    if (bufferIndex != boundChunk)
                    {
                        encoder.SetVertexBuffer(_pointChunks[bufferIndex], 0, 0);
                        if (useAttributePipeline)
                            encoder.SetVertexBuffer(_attributeChunks[bufferIndex], 0, 2);
                        boundChunk = bufferIndex;
                    }

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
                    MetalResources.Release(_uniformBuffers[i].NativePtr);
                _uniformBuffers[i] = default;

                if (_attributeUniformBuffers[i].NativePtr != IntPtr.Zero)
                    MetalResources.Release(_attributeUniformBuffers[i].NativePtr);
                _attributeUniformBuffers[i] = default;
            }
            MetalResources.Release(_classPaletteBuffer.NativePtr);
            MetalResources.Release(_pipeline.NativePtr);
            MetalResources.Release(_attributePipeline.NativePtr);
            MetalResources.Release(_depthState.NativePtr);
            _classPaletteBuffer = default;
            _pipeline = default;
            _attributePipeline = default;
            _depthState = default;
            _pendingData = null;
            _nodes = Array.Empty<PointOctreeNode>();
            _drawRanges = Array.Empty<PointDrawRange>();
            _lodScratch = new PointLodScratch(0);
        }

        // ── Private ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Storage mode for the cloud's buffers.
        /// </summary>
        /// <remarks>
        /// On a unified-memory device the CPU and the GPU read the same pages, so a shared
        /// buffer is written once; a managed one keeps a second copy on the GPU side and pays
        /// for a synchronize after every write. At a hundred million points that second copy
        /// is gigabytes of memory and seconds of load time for nothing.
        /// </remarks>
        private static MTLResourceOptions CloudStorageMode(MTLDevice device) =>
            device.HasUnifiedMemory
                ? MTLResourceOptions.ResourceStorageModeShared
                : MTLResourceOptions.ResourceStorageModeManaged;

        /// <summary>Publishes CPU writes to the GPU; only a managed buffer needs telling.</summary>
        private static void MarkUploaded(MTLDevice device, MTLBuffer buffer, ulong byteSize)
        {
            if (!device.HasUnifiedMemory)
                buffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });
        }

        private unsafe void UploadToGpu(PointCloudRenderData data, int residentCount, int[]? uploadOrder)
        {
            if (residentCount <= 0) return;

            var device = _context.Device;
            int chunkCount = PointRenderUploadBuilder.GetChunkCount(residentCount, PointsPerChunk);
            _pointChunks = new MTLBuffer[chunkCount];
            _chunkCounts = new int[chunkCount];

            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                int pointOffset = chunk * PointsPerChunk;
                int count = PointRenderUploadBuilder.GetChunkPointCount(residentCount, chunk, PointsPerChunk);
                ulong byteSize = (ulong)(count * PointStride);

                var buffer = device.NewBuffer(byteSize, CloudStorageMode(device));
                var destination = new Span<GpuPointVertex>(buffer.Contents.ToPointer(), count);
                PointRenderUploadBuilder.FillPoints(data, destination, pointOffset, uploadOrder);
                MarkUploaded(device, buffer, byteSize);

                _pointChunks[chunk] = buffer;
                _chunkCounts[chunk] = count;
            }
        }

        private unsafe void UploadAttributesToGpu(PointCloudRenderData data, int residentCount, int[]? uploadOrder)
        {
            if (residentCount <= 0)
                return;

            var device = _context.Device;
            int chunkCount = PointRenderUploadBuilder.GetChunkCount(residentCount, PointsPerChunk);
            _attributeChunks = new MTLBuffer[chunkCount];

            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                int pointOffset = chunk * PointsPerChunk;
                int count = PointRenderUploadBuilder.GetChunkPointCount(residentCount, chunk, PointsPerChunk);
                ulong byteSize = (ulong)(count * AttributeStride);

                var buffer = device.NewBuffer(byteSize, CloudStorageMode(device));
                var dst = (GpuPointAttribute*)buffer.Contents.ToPointer();
                PointRenderAttributeBuilder.Fill(
                    data, new Span<GpuPointAttribute>(dst, count), pointOffset, uploadOrder);

                MarkUploaded(device, buffer, byteSize);
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
                    MetalResources.Release(chunk.NativePtr);
            }
            _pointChunks = Array.Empty<MTLBuffer>();
            _chunkCounts = Array.Empty<int>();
        }

        private void ReleaseAttributeChunks()
        {
            foreach (var chunk in _attributeChunks)
            {
                if (chunk.NativePtr != IntPtr.Zero)
                    MetalResources.Release(chunk.NativePtr);
            }
            _attributeChunks = Array.Empty<MTLBuffer>();
        }
    }
}
