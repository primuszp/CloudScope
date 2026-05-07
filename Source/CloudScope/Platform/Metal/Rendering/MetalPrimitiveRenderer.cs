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

            ulong uniformSize = (ulong)Unsafe.SizeOf<MetalColorUniforms>();
            _uniformsBuffer = device.NewBuffer(uniformSize, MTLResourceOptions.ResourceStorageModeShared);

            _initialized = true;
        }

        public MTLBuffer CreateStaticBuffer(float[] vertices)
        {
            EnsureResources();
            if (vertices.Length == 0)
                return default;

            ulong byteSize = (ulong)(vertices.Length * sizeof(float));
            var buf = MetalFrameContext.Device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            unsafe
            {
                fixed (float* src = vertices)
                    Buffer.MemoryCopy(src, buf.Contents.ToPointer(), byteSize, byteSize);
            }
            buf.DidModifyRange(new NSRange { location = 0, length = buf.Length });
            return buf;
        }

        public void Draw(
            MTLBuffer vertexBuffer, int vertexCount,
            MTLPrimitiveType primitiveType,
            Matrix4 mvp, Vector4 color, bool depthTest)
        {
            if (vertexBuffer.NativePtr == IntPtr.Zero || vertexCount <= 0 || !_initialized)
                return;

            var cmdBuffer  = MetalFrameContext.CurrentCommandBuffer;
            var descriptor = MetalFrameContext.CurrentView?.CurrentRenderPassDescriptor;
            if (cmdBuffer.NativePtr == IntPtr.Zero || descriptor == null || descriptor.Value.NativePtr == IntPtr.Zero)
                return;

            MetalBufferWriter.Write(_uniformsBuffer, new MetalColorUniforms(mvp, color));

            var rpd = descriptor.Value;
            var ca = rpd.ColorAttachments.Object(0);
            ca.LoadAction = MTLLoadAction.Load;
            rpd.ColorAttachments.SetObject(ca, 0);

            var encoder = cmdBuffer.RenderCommandEncoder(rpd);
            encoder.SetRenderPipelineState(_pipeline);
            encoder.SetDepthStencilState(depthTest ? _depthOn : _depthOff);
            encoder.SetVertexBuffer(vertexBuffer, 0, 0);
            encoder.SetVertexBuffer(_uniformsBuffer, 0, 1);
            encoder.DrawPrimitives(primitiveType, 0, (ulong)vertexCount);
            encoder.EndEncoding();
        }

        public void Dispose()
        {
            // SharpMetal types are structs backed by ObjC ARC — nothing to release explicitly.
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
