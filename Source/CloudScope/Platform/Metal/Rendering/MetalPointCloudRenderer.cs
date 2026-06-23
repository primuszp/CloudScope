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
            PointData[] points = data.Points;
            int[]? renderOrder = data.RenderOrder;
            int requestedCount = data.Count;
            _pointCount = Math.Min(requestedCount, MaxResidentPoints);
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

            if (renderOrder is { Length: > 0 })
                UploadToGpu(points, _pointCount, renderOrder);
            else
                UploadToGpu(points, _pointCount);

            if (_hasAttributes)
                UploadAttributesToGpu(data, _pointCount);
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
                Math.Min(MaxDrawPointsPerFrame, _pointCount));

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
                    new MetalAttributePointUniforms(view, projection, pointSize, MapColorSource(_colorSource)));
                encoder.SetVertexBuffer(attributeUniformBuffer, 0, 1);
                encoder.SetVertexBuffer(_classPaletteBuffer, 0, 3);
            }
            else
            {
                MetalBufferWriter.Write(uniformBuffer, new MetalPointUniforms(view, projection, pointSize));
                encoder.SetVertexBuffer(uniformBuffer, 0, 1);
            }

            int remaining = drawCount;
            for (int i = 0; i < _pointChunks.Length && remaining > 0; i++)
            {
                int chunkDrawCount = Math.Min(_chunkCounts[i], remaining);
                if (chunkDrawCount <= 0)
                    break;

                encoder.SetVertexBuffer(_pointChunks[i], 0, 0);
                if (useAttributePipeline)
                    encoder.SetVertexBuffer(_attributeChunks[i], 0, 2);
                encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (ulong)chunkDrawCount);
                remaining -= chunkDrawCount;
            }
            return drawCount;
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

        private unsafe void UploadAttributesToGpu(PointCloudRenderData data, int residentCount)
        {
            if (residentCount <= 0)
                return;

            var attributes = data.Attributes
                ?? throw new InvalidOperationException("Point render attributes are missing.");
            int[] viewToSource = data.ViewToSource
                ?? throw new InvalidOperationException("Point render source map is missing.");

            var device = MetalFrameContext.Device;
            int chunkCount = (residentCount + PointsPerChunk - 1) / PointsPerChunk;
            _attributeChunks = new MTLBuffer[chunkCount];

            double zSpan = attributes.MaxZ - attributes.MinZ;
            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                int pointOffset = chunk * PointsPerChunk;
                int count = Math.Min(PointsPerChunk, residentCount - pointOffset);
                ulong byteSize = (ulong)(count * AttributeStride);

                var buffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
                var dst = (PointRenderAttributeData*)buffer.Contents.ToPointer();
                for (int i = 0; i < count; i++)
                {
                    int viewIndex = data.RenderOrder is { Length: > 0 } renderOrder ? renderOrder[pointOffset + i] : pointOffset + i;
                    int sourceIndex = viewToSource[viewIndex];
                    float zNormalized = zSpan > 0
                        ? (float)((attributes.Z[sourceIndex] - attributes.MinZ) / zSpan)
                        : 0.5f;
                    zNormalized = Math.Clamp(zNormalized, 0f, 1f);
                    float intensityNormalized = attributes.Intensity[sourceIndex] / 65535f;
                    PointData rgbSource = data.SourcePoints is { } sourcePoints
                        ? sourcePoints[sourceIndex]
                        : data.Points[viewIndex];
                    dst[i] = new PointRenderAttributeData(
                        zNormalized,
                        intensityNormalized,
                        attributes.Class[sourceIndex],
                        attributes.ReturnNumber[sourceIndex],
                        rgbSource.R,
                        rgbSource.G,
                        rgbSource.B);
                }

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

        private static int MapColorSource(ColorSource source) => source switch
        {
            ColorSource.Height => 1,
            ColorSource.Class => 2,
            ColorSource.Intensity => 3,
            ColorSource.Return => 4,
            _ => 0
        };

        [System.Runtime.InteropServices.DllImport("libobjc.dylib", EntryPoint = "objc_release")]
        private static extern void NativeRelease(IntPtr obj);

        private static void Release(IntPtr nativePtr)
        {
            if (nativePtr != IntPtr.Zero)
                NativeRelease(nativePtr);
        }
    }
}
