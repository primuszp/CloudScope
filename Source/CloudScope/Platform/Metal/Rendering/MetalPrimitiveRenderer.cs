using System;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalPrimitiveRenderer : IDisposable
    {

        private readonly MetalRenderContext _context;

        public MetalPrimitiveRenderer(MetalRenderContext context) => _context = context;
        private const int VertexByteSize = 3 * sizeof(float);
        private MetalFrameState? _frame;
        private MTLRenderPipelineState _pipeline;
        private MTLRenderPipelineState _wideLinePipeline;
        private MTLRenderPipelineState _joinedSegmentPipeline;
        private MTLRenderPipelineState _joinedJoinPipeline;
        private MTLDepthStencilState _depthOn;
        private MTLDepthStencilState _depthWrite;
        private MTLDepthStencilState _depthBehind;
        private MTLDepthStencilState _depthOff;
        private bool _initialized;

        public void SetFrame(MetalFrameState frame) => _frame = frame;

        public void EnsureResources()
        {
            if (_initialized)
                return;

            var device = _context.Device;
            var colorFmt = MTLPixelFormat.BGRA8Unorm;
            var depthFmt = MTLPixelFormat.Depth32Float;

            _pipeline = MetalShaderLibrary.CreateColorPipeline(device, colorFmt, depthFmt, _context.SampleCount);
            _wideLinePipeline = MetalShaderLibrary.CreateWideLinePipeline(device, colorFmt, depthFmt, _context.SampleCount);
            _joinedSegmentPipeline = MetalShaderLibrary.CreateJoinedLineSegmentPipeline(device, colorFmt, depthFmt, _context.SampleCount);
            _joinedJoinPipeline = MetalShaderLibrary.CreateJoinedLineJoinPipeline(device, colorFmt, depthFmt, _context.SampleCount);
            _depthOn  = MetalShaderLibrary.CreateDepthState(device, depthWrite: false);
            _depthWrite = MetalShaderLibrary.CreateDepthState(device, depthWrite: true);
            _depthBehind = MetalShaderLibrary.CreateDepthState(device, depthWrite: false, MTLCompareFunction.Greater);
            _depthOff = CreateDepthAlwaysState(device);

            _initialized = true;
        }

        public unsafe MTLBuffer CreateStaticBuffer(float[] vertices)
        {
            EnsureResources();
            if (vertices.Length == 0)
                return default;

            ulong byteSize = (ulong)(vertices.Length * sizeof(float));
            var buf = _context.Device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            fixed (float* src = vertices)
                Buffer.MemoryCopy(src, buf.Contents.ToPointer(), byteSize, byteSize);
            buf.DidModifyRange(new NSRange { location = 0, length = buf.Length });
            return buf;
        }

        public MTLBuffer CreateFrameBuffer(float[] vertices) => _context.UploadFrameBuffer(vertices);

        public void UpdateBuffer(ref MTLBuffer buffer, float[] vertices)
        {
            if (vertices.Length == 0) return;
            MTLBuffer next = CreateFrameBuffer(vertices);
            CloudScope.Platform.MacOS.ObjC.ObjectiveCRuntime.Retain(next.NativePtr);
            MetalResources.Release(ref buffer);
            buffer = next;
        }

        /// <param name="lineWidthPixels">
        /// Width for <see cref="MTLPrimitiveType.Line"/> draws. Metal has no line width, so
        /// anything wider than a pixel is expanded into screen-space quads by the shader
        /// (see <see cref="LineWidth"/>).
        /// </param>
        public void Draw(
            MTLBuffer vertexBuffer, int vertexCount,
            MTLPrimitiveType primitiveType,
            Matrix4 mvp, Vector4 color, bool depthTest,
            int firstVertex = 0,
            float lineWidthPixels = LineWidth.NativeMax, bool writeDepth = false)
        {
            if (vertexBuffer.NativePtr == IntPtr.Zero || vertexCount <= 0 || !_initialized)
                return;

            var encoder = _frame?.RenderCommandEncoder ?? default;
            if (encoder.NativePtr == IntPtr.Zero)
                return;

            bool expandLines = primitiveType == MTLPrimitiveType.Line
                && LineWidth.NeedsExpansion(lineWidthPixels);
            BindUniforms(encoder, mvp, color, lineWidthPixels);

            encoder.SetRenderPipelineState(expandLines ? _wideLinePipeline : _pipeline);
            encoder.SetDepthStencilState(depthTest ? (writeDepth ? _depthWrite : _depthOn) : _depthOff);

            if (expandLines)
            {
                // The shader indexes the buffer per instance, so the first vertex is applied
                // as a byte offset on the binding instead of a vertex start.
                encoder.SetVertexBuffer(vertexBuffer, (ulong)(firstVertex * VertexByteSize), 0);
                encoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip, 0, 4, (ulong)(vertexCount / 2));
                return;
            }

            encoder.SetVertexBuffer(vertexBuffer, 0, 0);
            encoder.DrawPrimitives(primitiveType, (ulong)firstVertex, (ulong)vertexCount);
        }

        /// <summary>
        /// Draws the overlap-free instance streams built by <see cref="PolylineRenderGeometry"/>:
        /// one screen-space quad per segment and one round-join fan per interior joint. This is
        /// the shared path for polylines, closed loops and gizmo circles; the expansion itself is
        /// <see cref="JoinedLineShaderCore"/>, which the OpenGL backend compiles from the same text.
        /// </summary>
        public void DrawJoinedLine(
            MTLBuffer segments, int segmentCount,
            MTLBuffer joins, int joinCount,
            Matrix4 mvp, Vector4 color, bool depthTest, float lineWidthPixels,
            bool occludedOnly = false, float dashPixels = 0f, bool depthBias = false)
        {
            if (segments.NativePtr == IntPtr.Zero || segmentCount <= 0 || !_initialized)
                return;

            var encoder = _frame?.RenderCommandEncoder ?? default;
            if (encoder.NativePtr == IntPtr.Zero)
                return;

            BindUniforms(encoder, mvp, color, lineWidthPixels, dashPixels);
            encoder.SetDepthStencilState(occludedOnly ? _depthBehind : depthTest ? _depthOn : _depthOff);
            // A line drawn on the surface of a point cloud wins or loses the depth test per
            // frame as streaming swaps the level of detail underneath it, which reads as the
            // stroke blinking. Biasing it towards the eye settles that without hiding a line
            // that genuinely runs behind the cloud.
            if (depthBias)
                encoder.SetDepthBias(-2f, -2f, 0f);

            encoder.SetRenderPipelineState(_joinedSegmentPipeline);
            encoder.SetVertexBuffer(segments, 0, 0);
            encoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip,
                0, (ulong)PolylineRenderGeometry.VerticesPerSegment, (ulong)segmentCount);

            if (joinCount > 0 && joins.NativePtr != IntPtr.Zero)
            {
                encoder.SetRenderPipelineState(_joinedJoinPipeline);
                encoder.SetVertexBuffer(joins, 0, 0);
                encoder.DrawPrimitives(MTLPrimitiveType.Triangle,
                    0, (ulong)PolylineRenderGeometry.VerticesPerJoin, (ulong)joinCount);
            }

            if (depthBias)
                encoder.SetDepthBias(0f, 0f, 0f);
        }

        private void BindUniforms(MTLRenderCommandEncoder encoder, Matrix4 mvp, Vector4 color,
            float lineWidthPixels, float dashPixels = 0f)
        {
            (int width, int height) = _context.ViewportSize;
            MetalDrawUniforms.Color(encoder, new MetalColorUniforms(mvp, color,
                new Vector4(width, height, lineWidthPixels, dashPixels)));
        }

        public void Dispose()
        {
            MetalResources.Release(_pipeline.NativePtr);
            MetalResources.Release(_wideLinePipeline.NativePtr);
            MetalResources.Release(_joinedSegmentPipeline.NativePtr);
            MetalResources.Release(_joinedJoinPipeline.NativePtr);
            MetalResources.Release(_depthOn.NativePtr);
            MetalResources.Release(_depthWrite.NativePtr);
            MetalResources.Release(_depthBehind.NativePtr);
            MetalResources.Release(_depthOff.NativePtr);
            _pipeline = default;
            _wideLinePipeline = default;
            _joinedSegmentPipeline = default;
            _joinedJoinPipeline = default;
            _depthOn = default;
            _depthWrite = default;
            _depthBehind = default;
            _depthOff = default;
            _initialized = false;
        }

        private static MTLDepthStencilState CreateDepthAlwaysState(MTLDevice device)
        {
            var desc = new MTLDepthStencilDescriptor();
            desc.DepthCompareFunction = MTLCompareFunction.Always;
            desc.IsDepthWriteEnabled = false;
            return device.NewDepthStencilState(desc);
        }
    }
}
