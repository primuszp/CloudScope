using System;
using System.Runtime.CompilerServices;
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
        private const int UniformStride = 256;
        private const int VertexByteSize = 3 * sizeof(float);
        private const int DrawsPerFrame = 512;
        private const int BufferedFrameCount = 3;
        private MetalFrameState? _frame;
        private MTLRenderPipelineState _pipeline;
        private MTLRenderPipelineState _wideLinePipeline;
        private MTLDepthStencilState _depthOn;
        private MTLDepthStencilState _depthOff;
        private MTLBuffer _uniformsBuffer;
        private int _uniformOffset;
        private int _bufferedFrameIndex = -1;
        private MetalFrameState? _uniformFrame;
        private bool _initialized;

        public void SetFrame(MetalFrameState frame)
        {
            _frame = frame;
            if (ReferenceEquals(_uniformFrame, frame))
                return;

            _uniformFrame = frame;
            _bufferedFrameIndex = (_bufferedFrameIndex + 1) % BufferedFrameCount;
            _uniformOffset = _bufferedFrameIndex * DrawsPerFrame;
        }

        public void EnsureResources()
        {
            if (_initialized)
                return;

            var device = _context.Device;
            var colorFmt = MTLPixelFormat.BGRA8Unorm;
            var depthFmt = MTLPixelFormat.Depth32Float;

            _pipeline = MetalShaderLibrary.CreateColorPipeline(device, colorFmt, depthFmt, _context.SampleCount);
            _wideLinePipeline = MetalShaderLibrary.CreateWideLinePipeline(device, colorFmt, depthFmt, _context.SampleCount);
            _depthOn  = MetalShaderLibrary.CreateDepthState(device, depthWrite: false);
            _depthOff = CreateDepthAlwaysState(device);

            _uniformsBuffer = device.NewBuffer(
                UniformStride * DrawsPerFrame * BufferedFrameCount,
                MTLResourceOptions.ResourceStorageModeShared);

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

        public unsafe void UpdateBuffer(ref MTLBuffer buffer, float[] vertices)
        {
            EnsureResources();
            if (vertices.Length == 0) return;

            ulong byteSize = (ulong)(vertices.Length * sizeof(float));
            if (buffer.NativePtr == IntPtr.Zero || buffer.Length < byteSize)
            {
                if (buffer.NativePtr != IntPtr.Zero)
                    MetalResources.Release(buffer.NativePtr);
                buffer = _context.Device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            }

            fixed (float* src = vertices)
                Buffer.MemoryCopy(src, buffer.Contents.ToPointer(), byteSize, byteSize);
            buffer.DidModifyRange(new NSRange { location = 0, length = byteSize });
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
            float lineWidthPixels = LineWidth.NativeMax)
        {
            if (vertexBuffer.NativePtr == IntPtr.Zero || vertexCount <= 0 || !_initialized)
                return;

            var encoder = _frame?.RenderCommandEncoder ?? default;
            if (encoder.NativePtr == IntPtr.Zero)
                return;

            ulong stride = UniformStride;
            ulong offset = (ulong)_uniformOffset * stride;
            int frameRegionEnd = (_bufferedFrameIndex + 1) * DrawsPerFrame;
            if (_uniformOffset >= frameRegionEnd || offset + stride > _uniformsBuffer.Length)
            {
                // A pathological gizmo frame exceeded its reserved region. Reuse the
                // last slot deterministically instead of corrupting another in-flight frame.
                _uniformOffset = frameRegionEnd - 1;
                offset = (ulong)_uniformOffset * stride;
            }

            bool expandLines = primitiveType == MTLPrimitiveType.Line
                && LineWidth.NeedsExpansion(lineWidthPixels);
            (int viewportWidth, int viewportHeight) = _context.ViewportSize;

            unsafe
            {
                byte* ptr = (byte*)_uniformsBuffer.Contents.ToPointer();
                var uniforms = new MetalColorUniforms(mvp, color,
                    new Vector4(viewportWidth, viewportHeight, lineWidthPixels, 0f));
                Buffer.MemoryCopy(&uniforms, ptr + offset, Unsafe.SizeOf<MetalColorUniforms>(), Unsafe.SizeOf<MetalColorUniforms>());
            }
            _uniformOffset++;

            encoder.SetRenderPipelineState(expandLines ? _wideLinePipeline : _pipeline);
            encoder.SetDepthStencilState(depthTest ? _depthOn : _depthOff);
            // ColorUniforms is consumed by both the vertex and the fragment function.
            // Binding only the vertex stage leaves the fragment color undefined,
            // making all primitive-based gizmos and line overlays disappear.
            encoder.SetVertexBuffer(_uniformsBuffer, offset, 1);
            encoder.SetFragmentBuffer(_uniformsBuffer, offset, 1);

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

        public void Dispose()
        {
            MetalResources.Release(ref _uniformsBuffer);
            MetalResources.Release(_pipeline.NativePtr);
            MetalResources.Release(_wideLinePipeline.NativePtr);
            MetalResources.Release(_depthOn.NativePtr);
            MetalResources.Release(_depthOff.NativePtr);
            _pipeline = default;
            _wideLinePipeline = default;
            _depthOn = default;
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
