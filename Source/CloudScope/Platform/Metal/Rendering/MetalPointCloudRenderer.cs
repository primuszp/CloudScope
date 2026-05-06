#if MACOS
using System;
using System.Runtime.CompilerServices;
using CloudScope.Rendering;
using Foundation;
using Metal;
using OpenTK.Mathematics;

namespace CloudScope.Platform.Metal
{
    internal sealed class MetalPointCloudRenderer : IPointCloudRenderer
    {
        private const int PointStride = 24;
        private IMTLRenderPipelineState? pipeline;
        private IMTLDepthStencilState? depthState;
        private IMTLBuffer? pointBuffer;
        private IMTLBuffer? uniformsBuffer;
        private int pointCount;

        public int PointCount => pointCount;

        public void Initialize()
        {
            var device = MetalFrameContext.CurrentView?.Device ?? MTLDevice.SystemDefault
                ?? throw new PlatformNotSupportedException("No Metal device is available.");

            pipeline = MetalShaderLibrary.CreatePointPipeline(
                device,
                MetalFrameContext.CurrentView?.ColorPixelFormat ?? MTLPixelFormat.BGRA8Unorm,
                MetalFrameContext.CurrentView?.DepthStencilPixelFormat ?? MTLPixelFormat.Depth32Float);
            depthState = MetalShaderLibrary.CreateDepthState(device, depthWriteEnabled: true);
            uniformsBuffer = device.CreateBuffer((UIntPtr)Unsafe.SizeOf<MetalPointUniforms>(), MTLResourceOptions.CpuCacheModeDefault);
        }

        public unsafe void Upload(PointData[] points)
        {
            pointCount = points.Length;
            if (pointCount == 0)
            {
                pointBuffer = null;
                return;
            }

            var device = MetalFrameContext.CurrentView?.Device ?? MTLDevice.SystemDefault
                ?? throw new PlatformNotSupportedException("No Metal device is available.");

            fixed (PointData* data = points)
            {
                pointBuffer = device.CreateBuffer(
                    (IntPtr)data,
                    (UIntPtr)(points.Length * PointStride),
                    MTLResourceOptions.StorageModeManaged);
            }
        }

        public int Render(ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius)
        {
            if (pointCount <= 0 || pointBuffer == null || pipeline == null || uniformsBuffer == null)
                return 0;

            var commandBuffer = MetalFrameContext.CurrentCommandBuffer;
            var descriptor = MetalFrameContext.CurrentView?.CurrentRenderPassDescriptor;
            if (commandBuffer == null || descriptor == null)
                return 0;

            float subsampleRatio = (float)(cloudRadius / Math.Max(halfViewSize, cloudRadius * 0.001));
            float subsampleFactor = Math.Clamp(subsampleRatio * subsampleRatio, 0.005f, 1f);
            int drawCount = Math.Max((int)(pointCount * subsampleFactor), Math.Min(100_000, pointCount));

            MetalBufferWriter.Write(uniformsBuffer, new MetalPointUniforms(view, projection, pointSize));

            using var encoder = commandBuffer.CreateRenderCommandEncoder(descriptor);
            encoder.SetRenderPipelineState(pipeline);
            if (depthState != null)
                encoder.SetDepthStencilState(depthState);
            encoder.SetVertexBuffer(pointBuffer, UIntPtr.Zero, 0);
            encoder.SetVertexBuffer(uniformsBuffer, UIntPtr.Zero, 1);
            encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (UIntPtr)drawCount);
            encoder.EndEncoding();

            return drawCount;
        }

        public void Dispose()
        {
            pointBuffer?.Dispose();
            uniformsBuffer?.Dispose();
            pipeline?.Dispose();
            depthState?.Dispose();
        }
    }
}
#endif
