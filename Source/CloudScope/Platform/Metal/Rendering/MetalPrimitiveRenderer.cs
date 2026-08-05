using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using OpenTK.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalPrimitiveRenderer : IDisposable
    {
        private const int UniformStride = 256;
        private const int DrawsPerFrame = 512;
        private const int BufferedFrameCount = 3;
        private MetalFrameState? _frame;
        private MTLRenderPipelineState _pipeline;
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

            var device = MetalFrameContext.Device;
            var colorFmt = MTLPixelFormat.BGRA8Unorm;
            var depthFmt = MTLPixelFormat.Depth32Float;

            _pipeline = MetalShaderLibrary.CreateColorPipeline(device, colorFmt, depthFmt);
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
            var buf = MetalFrameContext.Device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
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
                    NativeRelease(buffer.NativePtr);
                buffer = MetalFrameContext.Device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            }

            fixed (float* src = vertices)
                Buffer.MemoryCopy(src, buffer.Contents.ToPointer(), byteSize, byteSize);
            buffer.DidModifyRange(new NSRange { location = 0, length = byteSize });
        }

        public void Draw(
            MTLBuffer vertexBuffer, int vertexCount,
            MTLPrimitiveType primitiveType,
            Matrix4 mvp, Vector4 color, bool depthTest,
            int firstVertex = 0)
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

            unsafe
            {
                byte* ptr = (byte*)_uniformsBuffer.Contents.ToPointer();
                var uniforms = new MetalColorUniforms(mvp, color);
                Buffer.MemoryCopy(&uniforms, ptr + offset, Unsafe.SizeOf<MetalColorUniforms>(), Unsafe.SizeOf<MetalColorUniforms>());
            }
            _uniformOffset++;

            encoder.SetRenderPipelineState(_pipeline);
            encoder.SetDepthStencilState(depthTest ? _depthOn : _depthOff);
            encoder.SetVertexBuffer(vertexBuffer, 0, 0);
            encoder.SetVertexBuffer(_uniformsBuffer, offset, 1);
            // ColorUniforms is consumed by both color_vertex and color_fragment.
            // Binding only the vertex stage leaves the fragment color undefined,
            // making all primitive-based gizmos and line overlays disappear.
            encoder.SetFragmentBuffer(_uniformsBuffer, offset, 1);
            encoder.DrawPrimitives(primitiveType, (ulong)firstVertex, (ulong)vertexCount);
        }

        public void Dispose()
        {
            Release(ref _uniformsBuffer);
            Release(_pipeline.NativePtr);
            Release(_depthOn.NativePtr);
            Release(_depthOff.NativePtr);
            _pipeline = default;
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

        [System.Runtime.InteropServices.DllImport("libobjc.dylib", EntryPoint = "objc_release")]
        private static extern void NativeRelease(IntPtr obj);

        public static void Release(ref MTLBuffer buffer)
        {
            if (buffer.NativePtr == IntPtr.Zero)
                return;

            Release(buffer.NativePtr);
            buffer = default;
        }

        private static void Release(IntPtr nativePtr)
        {
            if (nativePtr != IntPtr.Zero)
                NativeRelease(nativePtr);
        }
    }
}
