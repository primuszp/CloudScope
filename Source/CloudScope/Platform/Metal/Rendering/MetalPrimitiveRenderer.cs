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
        private MTLRenderPipelineState _pipeline;
        private MTLDepthStencilState _depthOn;
        private MTLDepthStencilState _depthOff;
        private MTLBuffer _uniformsBuffer;
        private int _uniformOffset;
        private bool _initialized;

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

            ulong uniformSize = 256; // Must be 256-byte aligned for offset
            _uniformsBuffer = device.NewBuffer(uniformSize * 256, MTLResourceOptions.ResourceStorageModeShared); // enough for 256 draw calls per frame

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
                buffer = MetalFrameContext.Device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            }

            fixed (float* src = vertices)
                Buffer.MemoryCopy(src, buffer.Contents.ToPointer(), byteSize, byteSize);
            buffer.DidModifyRange(new NSRange { location = 0, length = byteSize });
        }

        public void Draw(
            MTLBuffer vertexBuffer, int vertexCount,
            MTLPrimitiveType primitiveType,
            Matrix4 mvp, Vector4 color, bool depthTest)
        {
            if (vertexBuffer.NativePtr == IntPtr.Zero || vertexCount <= 0 || !_initialized)
                return;

            var encoder = MetalFrameContext.CurrentRenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero)
                return;

            ulong stride = 256;
            ulong offset = (ulong)(_uniformOffset * stride);
            if (offset + stride > _uniformsBuffer.Length)
            {
                _uniformOffset = 0;
                offset = 0;
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
            encoder.DrawPrimitives(primitiveType, 0, (ulong)vertexCount);
        }

        public void Dispose()
        {
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
