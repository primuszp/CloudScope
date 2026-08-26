using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CloudScope.Labeling;
using CloudScope.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalHighlightRenderer : IHighlightRenderer
    {
        private readonly MetalRenderContext _context;

        public MetalHighlightRenderer(MetalRenderContext context) => _context = context;

        private MTLRenderPipelineState _pipeline;
        private MTLDepthStencilState _depthState;
        private MTLBuffer _uniformsBuffer;
        private MTLBuffer _highlightBuffer;
        private MTLBuffer _previewBuffer;
        private bool _dirty = true;
        private int _highlightCount;
        private int _previewCount;
        private PointData[] _scratch = Array.Empty<PointData>();

        public void MarkDirty() => _dirty = true;

        public void UpdatePreview(PointData[]? points, IReadOnlyList<int>? indices)
        {
            EnsureResources();
            if (points == null || indices == null || indices.Count == 0)
            {
                _previewCount = 0;
                return;
            }

            var data = RentScratch(indices.Count);
            int count = HighlightPointBuilder.FillPreview(points, indices, data);
            _previewCount = count;
            BuildBuffer(ref _previewBuffer, data, count);
        }

        public void RenderPreview(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj, float pointSize)
            => RenderBuffer(frameData, _previewBuffer, _previewCount, ref view, ref proj, pointSize + 2f);

        public void Render(IRenderFrameData frameData, PointData[] points, LabelManager labels, Func<PointAnnotation, Vector3> annotationColor, ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            EnsureResources();
            if (_dirty)
            {
                RebuildHighlightBuffer(points, labels, annotationColor);
                _dirty = false;
            }
            RenderBuffer(frameData, _highlightBuffer, _highlightCount, ref view, ref proj, pointSize + 2f);
        }

        public void Dispose()
        {
            MetalResources.Release(ref _uniformsBuffer);
            MetalResources.Release(ref _highlightBuffer);
            MetalResources.Release(ref _previewBuffer);
            MetalResources.Release(_pipeline.NativePtr);
            MetalResources.Release(_depthState.NativePtr);
            _pipeline = default;
            _depthState = default;
            _scratch = Array.Empty<PointData>();
            _highlightCount = 0;
            _previewCount = 0;
            _dirty = true;
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void EnsureResources()
        {
            if (_pipeline.NativePtr != IntPtr.Zero)
                return;

            var device = _context.Device;
            _pipeline    = MetalShaderLibrary.CreatePointPipeline(device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float);
            _depthState  = MetalShaderLibrary.CreateDepthState(device, depthWrite: false);
            _uniformsBuffer = device.NewBuffer(
                (ulong)Unsafe.SizeOf<MetalPointUniforms>(),
                MTLResourceOptions.ResourceStorageModeShared);
        }

        private void RebuildHighlightBuffer(PointData[] points, LabelManager labels, Func<PointAnnotation, Vector3> annotationColor)
        {
            var allAnnotations = labels.AllAnnotations;
            var data  = RentScratch(allAnnotations.Count);
            int count = HighlightPointBuilder.FillAnnotations(points, allAnnotations, annotationColor, data);
            _highlightCount  = count;
            BuildBuffer(ref _highlightBuffer, data, count);
        }

        private void RenderBuffer(IRenderFrameData frameData, MTLBuffer buffer, int count,
            ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            if (count == 0 || buffer.NativePtr == IntPtr.Zero || _pipeline.NativePtr == IntPtr.Zero)
                return;

            MetalBufferWriter.Write(_uniformsBuffer, new MetalPointUniforms(view, proj, pointSize));

            var encoder = frameData is MetalFrameState frame
                ? frame.RenderCommandEncoder
                : default;
            if (encoder.NativePtr == IntPtr.Zero) return;

            encoder.SetRenderPipelineState(_pipeline);
            encoder.SetDepthStencilState(_depthState);
            encoder.SetVertexBuffer(buffer, 0, 0);
            encoder.SetVertexBuffer(_uniformsBuffer, 0, 1);
            encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (ulong)count);
        }

        private unsafe void BuildBuffer(ref MTLBuffer existingBuffer, PointData[] data, int count)
        {
            if (count == 0) return;
            ulong byteSize = (ulong)(count * 24);
            var device = _context.Device;

            if (existingBuffer.NativePtr == IntPtr.Zero || existingBuffer.Length < byteSize)
            {
                if (existingBuffer.NativePtr != IntPtr.Zero)
                    MetalResources.Release(existingBuffer.NativePtr);
                existingBuffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            }

            fixed (PointData* src = data)
                Buffer.MemoryCopy(src, existingBuffer.Contents.ToPointer(), byteSize, byteSize);
            existingBuffer.DidModifyRange(new NSRange { location = 0, length = byteSize });
        }

        private PointData[] RentScratch(int count)
        {
            if (_scratch.Length < count)
                _scratch = new PointData[count];
            return _scratch;
        }
    }
}
