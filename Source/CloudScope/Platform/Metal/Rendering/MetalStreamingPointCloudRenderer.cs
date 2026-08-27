using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CloudScope.Loading;
using CloudScope.Rendering;
using CloudScope.Store;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    /// <summary>
    /// Draws an on-disk cloud by keeping the visible cells in a fixed set of Metal buffers.
    /// </summary>
    /// <remarks>
    /// The Metal counterpart of <see cref="OpenGlStreamingPointCloudRenderer"/>, and the same
    /// shape: the same traversal picks the cells, the same page table places them, and the
    /// same residency decides what is worth keeping. Only the write differs — on a unified
    /// memory device a page lands in the buffer with a plain copy, no staging and no transfer.
    /// </remarks>
    [SupportedOSPlatform("macos")]
    internal sealed class MetalStreamingPointCloudRenderer : IPointTileCloudRenderer, IPointTilePageBuffer
    {
        /// <summary>Points per page; see <see cref="OpenGlStreamingPointCloudRenderer"/>.</summary>
        private const int PageSizeInPoints = 64 * 1024;

        /// <summary>
        /// Points per Metal buffer. The working set is split the same way the resident path
        /// splits a cloud, because a device's maximum buffer length is well below the memory
        /// it will keep resident. A page divides a chunk exactly, so no page straddles two
        /// buffers and every draw range stays inside one.
        /// </summary>
        private const int PointsPerChunk = PointRenderUploadBuilder.DefaultPointsPerChunk
            / PageSizeInPoints * PageSizeInPoints;

        private const int PagesPerChunk = PointsPerChunk / PageSizeInPoints;

        private static PointRenderLimits Limits => PointRenderLimits.For("METAL");

        private const int UniformBufferCount = 3;

        private readonly MetalRenderContext _context;
        private readonly MTLBuffer[] _uniformBuffers = new MTLBuffer[UniformBufferCount];
        private readonly MTLBuffer[] _attributeUniformBuffers = new MTLBuffer[UniformBufferCount];
        private readonly PointTileTraversalScratch _scratch = new();

        private MTLRenderPipelineState _pipeline;
        private MTLRenderPipelineState _attributePipeline;
        private MTLDepthStencilState _depthState;
        private MTLBuffer _classPaletteBuffer;
        private MTLBuffer[] _pointChunks = Array.Empty<MTLBuffer>();
        private MTLBuffer[] _attributeChunks = Array.Empty<MTLBuffer>();
        private int _uniformBufferIndex;

        private IReadOnlyList<PointTileLayer> _layers = Array.Empty<PointTileLayer>();
        private PointTileResidency? _residency;
        private PointTilePageTable? _pages;
        private PointTileVisit[] _visits = Array.Empty<PointTileVisit>();
        private PointDrawRange[] _ranges = Array.Empty<PointDrawRange>();
        private PointTileLayerBatch[] _batches = Array.Empty<PointTileLayerBatch>();

        private bool _hasAttributes;
        private ColorSource _colorSource = ColorSource.Rgb;

        /// <summary>Layers attached before the device was ready; opened on <see cref="Initialize"/>.</summary>
        private IReadOnlyList<PointTileLayer>? _pendingLayers;

        public MetalStreamingPointCloudRenderer(MetalRenderContext context) => _context = context;

        public long DrawnPointCount { get; private set; }

        public int ResidentCellCount => _residency?.ResidentCellCount ?? 0;

        public int PendingCellCount => _residency?.PendingCellCount ?? 0;

        public void Initialize()
        {
            MTLDevice device = _context.Device;

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
                _attributeUniformBuffers[i] = device.NewBuffer(
                    attributeUniformSize, MTLResourceOptions.ResourceStorageModeShared);
            }

            UploadClassPalette(device);

            if (_pendingLayers is { } pending)
            {
                _pendingLayers = null;
                Open(pending);
            }
        }

        public void Open(IReadOnlyList<PointTileLayer> layers)
        {
            Release();
            if (layers.Count == 0)
                return;

            _layers = layers;
            // Every layer's store must agree on whether attributes exist, since they share one
            // set of buffers and one vertex layout.
            _hasAttributes = layers.All(layer => layer.Store.Header.HasAttributes);

            // Device not ready yet — the layers are attached once Initialize has run.
            if (_context.Device.NativePtr == IntPtr.Zero)
            {
                _pendingLayers = layers;
                return;
            }

            MTLDevice device = _context.Device;
            int pageCount = ResolvePageCount(layers, _hasAttributes, device);
            _pages = new PointTilePageTable(PageSizeInPoints, pageCount);

            int cellCount = layers.Sum(layer => layer.Store.Nodes.Length);
            _visits = new PointTileVisit[cellCount];
            _ranges = new PointDrawRange[pageCount + cellCount];
            _batches = new PointTileLayerBatch[layers.Count];

            int chunkCount = (pageCount + PagesPerChunk - 1) / PagesPerChunk;
            _pointChunks = new MTLBuffer[chunkCount];
            _attributeChunks = _hasAttributes ? new MTLBuffer[chunkCount] : Array.Empty<MTLBuffer>();
            MTLResourceOptions storage = CloudStorageMode(device);
            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                _pointChunks[chunk] = device.NewBuffer((ulong)PointsPerChunk * GpuPointVertex.Stride, storage);
                if (_hasAttributes)
                {
                    _attributeChunks[chunk] = device.NewBuffer(
                        (ulong)PointsPerChunk * GpuPointAttribute.Stride, storage);
                }
            }

            _residency = new PointTileResidency(layers, this, _pages);
        }

        /// <summary>
        /// How many pages the working set may hold: what the device recommends keeping
        /// resident, never more than the clouds themselves need.
        /// </summary>
        private static int ResolvePageCount(
            IReadOnlyList<PointTileLayer> layers, bool hasAttributes, MTLDevice device)
        {
            int bytesPerPoint = GpuPointVertex.Stride + (hasAttributes ? GpuPointAttribute.Stride : 0);
            long totalPoints = layers.Sum(layer => layer.Store.Header.PointCount);
            long residentPoints = Math.Min(
                totalPoints,
                Limits.ResolveResidentLimit((long)device.RecommendedMaxWorkingSetSize, bytesPerPoint));
            int pageCount = (int)Math.Max(1, residentPoints / PageSizeInPoints);
            // Buffers are allocated whole, so the table is rounded up to fill the last one
            // rather than leaving part of an allocation unusable.
            return (pageCount + PagesPerChunk - 1) / PagesPerChunk * PagesPerChunk;
        }

        public void Close() => Release();

        public void UpdateColorSource(ColorSource source) => _colorSource = source;

        /// <summary>Copies one page of a cell into the buffer that holds it.</summary>
        public unsafe void WritePage(
            int firstPoint,
            ReadOnlySpan<GpuPointVertex> points,
            ReadOnlySpan<GpuPointAttribute> attributes)
        {
            int chunk = firstPoint / PointsPerChunk;
            int offset = firstPoint - chunk * PointsPerChunk;
            if ((uint)chunk >= (uint)_pointChunks.Length)
                return;

            MTLDevice device = _context.Device;
            MTLBuffer pointBuffer = _pointChunks[chunk];
            points.CopyTo(new Span<GpuPointVertex>(pointBuffer.Contents.ToPointer(), PointsPerChunk)[offset..]);
            MarkUploaded(device, pointBuffer, (ulong)offset * GpuPointVertex.Stride,
                (ulong)points.Length * GpuPointVertex.Stride);

            if (attributes.IsEmpty || _attributeChunks.Length == 0)
                return;

            MTLBuffer attributeBuffer = _attributeChunks[chunk];
            attributes.CopyTo(
                new Span<GpuPointAttribute>(attributeBuffer.Contents.ToPointer(), PointsPerChunk)[offset..]);
            MarkUploaded(device, attributeBuffer, (ulong)offset * GpuPointAttribute.Stride,
                (ulong)attributes.Length * GpuPointAttribute.Stride);
        }

        public int Render(IRenderFrameData frameData, in PointRenderView renderView)
        {
            DrawnPointCount = 0;
            if (_residency is null || _pages is null || _layers.Count == 0
                || _pipeline.NativePtr == IntPtr.Zero)
                return 0;

            Matrix4 view = renderView.View;
            Matrix4 projection = renderView.Projection;
            if (float.IsNaN(view.M11) || float.IsNaN(projection.M11))
                return 0;

            if (frameData is not MetalFrameState frame
                || frame.CommandBuffer.NativePtr == IntPtr.Zero
                || frame.RenderPassDescriptor.NativePtr == IntPtr.Zero)
                return 0;

            long totalPoints = 0;
            foreach (PointTileLayer layer in _layers)
                totalPoints += layer.Store.Header.PointCount;

            int drawBudget = Limits.GetFrameBudget((int)Math.Min(totalPoints, int.MaxValue));
            int visitCount = PointTileTraversal.Collect(
                _layers, in renderView, drawBudget, _visits, _scratch, out _);

            // Page writes happen inside BeginFrame, so it runs even when nothing is visible:
            // that is what lets a cloud finish streaming in while the camera sits still.
            int batchCount = _residency.BeginFrame(
                _visits.AsSpan(0, visitCount), _ranges, _batches, out long drawnPointCount);
            DrawnPointCount = drawnPointCount;
            if (batchCount == 0)
                return 0;

            MTLRenderCommandEncoder encoder = frame.RenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero)
                return 0;

            bool useAttributePipeline = _hasAttributes
                && _attributeChunks.Length == _pointChunks.Length
                && _attributePipeline.NativePtr != IntPtr.Zero
                && _classPaletteBuffer.NativePtr != IntPtr.Zero;

            encoder.SetRenderPipelineState(useAttributePipeline ? _attributePipeline : _pipeline);
            encoder.SetDepthStencilState(_depthState);

            for (int batch = 0; batch < batchCount; batch++)
            {
                PointTileLayerBatch layerBatch = _batches[batch];
                Vector3 tint = _layers[layerBatch.LayerIndex].Tint;

                // A layer's tint travels in its own uniform buffer, so the buffers a frame
                // still has in flight are never rewritten underneath it.
                _uniformBufferIndex = (_uniformBufferIndex + 1) % UniformBufferCount;
                if (useAttributePipeline)
                {
                    MTLBuffer attributeUniformBuffer = _attributeUniformBuffers[_uniformBufferIndex];
                    MetalBufferWriter.Write(
                        attributeUniformBuffer,
                        new MetalAttributePointUniforms(
                            view, projection, renderView.PointSize,
                            PointRenderAttributeBuilder.MapColorSource(_colorSource), tint));
                    encoder.SetVertexBuffer(attributeUniformBuffer, 0, 1);
                    encoder.SetVertexBuffer(_classPaletteBuffer, 0, 3);
                }
                else
                {
                    MTLBuffer uniformBuffer = _uniformBuffers[_uniformBufferIndex];
                    MetalBufferWriter.Write(
                        uniformBuffer,
                        new MetalPointUniforms(view, projection, renderView.PointSize, 1f, tint));
                    encoder.SetVertexBuffer(uniformBuffer, 0, 1);
                }

                // A range never straddles two buffers, and the pages of one cell usually share
                // one, so rebinding only on a change keeps the encoder calls down.
                int boundChunk = -1;
                for (int i = 0; i < layerBatch.RangeCount; i++)
                {
                    PointDrawRange range = _ranges[layerBatch.FirstRange + i];
                    int chunk = range.First / PointsPerChunk;
                    int firstInChunk = range.First - chunk * PointsPerChunk;
                    if ((uint)chunk >= (uint)_pointChunks.Length)
                        continue;

                    if (chunk != boundChunk)
                    {
                        encoder.SetVertexBuffer(_pointChunks[chunk], 0, 0);
                        if (useAttributePipeline)
                            encoder.SetVertexBuffer(_attributeChunks[chunk], 0, 2);
                        boundChunk = chunk;
                    }

                    encoder.DrawPrimitives(MTLPrimitiveType.Point, (ulong)firstInChunk, (ulong)range.Count);
                }
            }

            return (int)Math.Min(drawnPointCount, int.MaxValue);
        }

        public void Dispose()
        {
            Release();

            for (int i = 0; i < UniformBufferCount; i++)
            {
                MetalResources.Release(_uniformBuffers[i].NativePtr);
                _uniformBuffers[i] = default;
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
        }

        private void Release()
        {
            // The residency frees its pages against the table, so it goes first.
            _residency?.Dispose();
            _residency = null;
            _pages = null;
            _layers = Array.Empty<PointTileLayer>();
            _pendingLayers = null;

            foreach (MTLBuffer chunk in _pointChunks)
                MetalResources.Release(chunk.NativePtr);
            foreach (MTLBuffer chunk in _attributeChunks)
                MetalResources.Release(chunk.NativePtr);

            _pointChunks = Array.Empty<MTLBuffer>();
            _attributeChunks = Array.Empty<MTLBuffer>();
            _visits = Array.Empty<PointTileVisit>();
            _ranges = Array.Empty<PointDrawRange>();
            _batches = Array.Empty<PointTileLayerBatch>();
        }

        /// <summary>
        /// Storage mode for the working set: shared where the CPU and the GPU read the same
        /// pages, managed otherwise — the same reasoning as the resident path.
        /// </summary>
        private static MTLResourceOptions CloudStorageMode(MTLDevice device) =>
            device.HasUnifiedMemory
                ? MTLResourceOptions.ResourceStorageModeShared
                : MTLResourceOptions.ResourceStorageModeManaged;

        /// <summary>Publishes a page's bytes to the GPU; only a managed buffer needs telling.</summary>
        private static void MarkUploaded(MTLDevice device, MTLBuffer buffer, ulong offset, ulong length)
        {
            if (!device.HasUnifiedMemory)
                buffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = offset, length = length });
        }

        private unsafe void UploadClassPalette(MTLDevice device)
        {
            const int PaletteSize = 256;
            ulong byteSize = (ulong)(PaletteSize * Unsafe.SizeOf<MetalPaletteColor>());
            _classPaletteBuffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            var destination = (MetalPaletteColor*)_classPaletteBuffer.Contents.ToPointer();
            for (int i = 0; i < PaletteSize; i++)
            {
                Vector3 color = ClassColorPalette.GetColor((byte)i);
                destination[i] = new MetalPaletteColor(color.X, color.Y, color.Z);
            }

            _classPaletteBuffer.DidModifyRange(
                new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });
        }
    }
}
