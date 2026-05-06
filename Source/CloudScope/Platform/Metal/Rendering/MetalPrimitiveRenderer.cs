#if MACOS
using System;
using Metal;
using OpenTK.Mathematics;

namespace CloudScope.Platform.Metal
{
    internal sealed class MetalPrimitiveRenderer : IDisposable
    {
        private IMTLRenderPipelineState? pipeline;
        private IMTLDepthStencilState? depthOn;
        private IMTLDepthStencilState? depthOff;
        private IMTLBuffer? uniformsBuffer;

        public void EnsureResources()
        {
            if (pipeline != null)
                return;

            var view = MetalFrameContext.CurrentView;
            var device = view?.Device ?? MTLDevice.SystemDefault
                ?? throw new PlatformNotSupportedException("No Metal device is available.");
            pipeline = MetalShaderLibrary.CreateColorPipeline(
                device,
                view?.ColorPixelFormat ?? MTLPixelFormat.BGRA8Unorm,
                view?.DepthStencilPixelFormat ?? MTLPixelFormat.Depth32Float);
            depthOn = MetalShaderLibrary.CreateDepthState(device, depthWriteEnabled: false);
            depthOff = CreateDepthOffState(device);
            uniformsBuffer = device.CreateBuffer((UIntPtr)System.Runtime.CompilerServices.Unsafe.SizeOf<MetalColorUniforms>(), MTLResourceOptions.CpuCacheModeDefault);
        }

        public unsafe IMTLBuffer? CreateStaticBuffer(float[] vertices)
        {
            EnsureResources();
            if (vertices.Length == 0)
                return null;

            var device = MetalFrameContext.CurrentView?.Device ?? MTLDevice.SystemDefault
                ?? throw new PlatformNotSupportedException("No Metal device is available.");
            fixed (float* ptr = vertices)
            {
                return device.CreateBuffer((IntPtr)ptr, (UIntPtr)(vertices.Length * sizeof(float)), MTLResourceOptions.StorageModeManaged);
            }
        }

        public void Draw(IMTLBuffer? vertexBuffer, int vertexCount, MTLPrimitiveType primitiveType, Matrix4 mvp, Vector4 color, bool depthTest)
        {
            if (vertexBuffer == null || vertexCount <= 0)
                return;

            EnsureResources();
            var commandBuffer = MetalFrameContext.CurrentCommandBuffer;
            var descriptor = MetalFrameContext.CurrentView?.CurrentRenderPassDescriptor;
            if (commandBuffer == null || descriptor == null || pipeline == null || uniformsBuffer == null)
                return;

            descriptor.ColorAttachments[0].LoadAction = MTLLoadAction.Load;
            MetalBufferWriter.Write(uniformsBuffer, new MetalColorUniforms(mvp, color));

            using var encoder = commandBuffer.CreateRenderCommandEncoder(descriptor);
            encoder.SetRenderPipelineState(pipeline);
            encoder.SetDepthStencilState(depthTest ? depthOn : depthOff);
            encoder.SetVertexBuffer(vertexBuffer, UIntPtr.Zero, 0);
            encoder.SetVertexBuffer(uniformsBuffer, UIntPtr.Zero, 1);
            encoder.DrawPrimitives(primitiveType, 0, (UIntPtr)vertexCount);
            encoder.EndEncoding();
        }

        public void Dispose()
        {
            pipeline?.Dispose();
            depthOn?.Dispose();
            depthOff?.Dispose();
            uniformsBuffer?.Dispose();
        }

        private static IMTLDepthStencilState? CreateDepthOffState(IMTLDevice device)
        {
            using var descriptor = new MTLDepthStencilDescriptor
            {
                DepthCompareFunction = MTLCompareFunction.Always,
                DepthWriteEnabled = false
            };
            return device.CreateDepthStencilState(descriptor);
        }
    }
}
#endif
