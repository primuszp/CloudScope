using System;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CloudScope.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalPointCloudRenderer : IPointCloudRenderer
    {
        private const int PointStride = 24; // 6 floats

        // Triple-buffered uniforms — CPU never blocks waiting for GPU to finish.
        private const int UniformBufferCount = 3;
        private readonly MTLBuffer[] _uniformBuffers = new MTLBuffer[UniformBufferCount];
        private int _uniformBufferIndex;

        private MTLRenderPipelineState _pipeline;
        private MTLDepthStencilState _depthState;
        private MTLBuffer _pointBuffer;
        private int _pointCount;

        // Points uploaded before Initialize() — deferred to first Initialize() call.
        private PointData[]? _pendingPoints;

        public int PointCount => _pointCount;

        public void Initialize()
        {
            var device = MetalFrameContext.Device;

            _pipeline = MetalShaderLibrary.CreatePointPipeline(
                device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float);
            _depthState = MetalShaderLibrary.CreateDepthState(device, depthWrite: true);

            ulong uniformSize = (ulong)Unsafe.SizeOf<MetalPointUniforms>();
            for (int i = 0; i < UniformBufferCount; i++)
                _uniformBuffers[i] = device.NewBuffer(uniformSize, MTLResourceOptions.ResourceStorageModeShared);

            // Flush any points that arrived before the Metal device was ready.
            if (_pendingPoints != null)
            {
                UploadToGpu(_pendingPoints);
                _pendingPoints = null;
            }
        }

        public void Upload(PointData[] points)
        {
            _pointCount = points.Length;
            if (_pointBuffer.NativePtr != IntPtr.Zero)
                NativeRelease(_pointBuffer.NativePtr);
            _pointBuffer = default;

            // Device not ready yet (called before Initialize / app.Run) — defer.
            if (MetalFrameContext.Device.NativePtr == IntPtr.Zero)
            {
                _pendingPoints = points;
                return;
            }

            UploadToGpu(points);
        }

        private int _renderCallCount;

        public int Render(ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius)
        {
            _renderCallCount++;
            bool log = _renderCallCount <= 3 || _renderCallCount % 60 == 0;

            if (_pointCount <= 0 || _pointBuffer.NativePtr == IntPtr.Zero || _pipeline.NativePtr == IntPtr.Zero)
            {
                if (log) Console.WriteLine($"[PCR {_renderCallCount}] BAIL: count={_pointCount} buf={_pointBuffer.NativePtr != IntPtr.Zero} pipe={_pipeline.NativePtr != IntPtr.Zero}");
                return 0;
            }

            var cmdBuffer  = MetalFrameContext.CurrentCommandBuffer;
            var descriptor = MetalFrameContext.CurrentRenderPassDescriptor;
            if (cmdBuffer.NativePtr == IntPtr.Zero || descriptor.NativePtr == IntPtr.Zero)
            {
                if (log) Console.WriteLine($"[PCR {_renderCallCount}] BAIL: cmd={cmdBuffer.NativePtr != IntPtr.Zero} desc={descriptor.NativePtr != IntPtr.Zero}");
                return 0;
            }

            float subsampleRatio  = (float)(cloudRadius / Math.Max(halfViewSize, cloudRadius * 0.001));
            float subsampleFactor = Math.Clamp(subsampleRatio * subsampleRatio, 0.005f, 1f);
            int drawCount = Math.Max((int)(_pointCount * subsampleFactor), Math.Min(100_000, _pointCount));

            _uniformBufferIndex = (_uniformBufferIndex + 1) % UniformBufferCount;
            var uniformBuffer = _uniformBuffers[_uniformBufferIndex];
            MetalBufferWriter.Write(uniformBuffer, new MetalPointUniforms(view, projection, pointSize));

            var encoder = MetalFrameContext.CurrentRenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero)
            {
                if (log) Console.WriteLine($"[PCR {_renderCallCount}] encoder NULL");
                return 0;
            }

            if (log) Console.WriteLine($"[PCR {_renderCallCount}] draw={drawCount} hvs={halfViewSize:F1} sf={subsampleFactor:F3}");

            encoder.SetRenderPipelineState(_pipeline);
            encoder.SetDepthStencilState(_depthState);
            encoder.SetVertexBuffer(_pointBuffer, 0, 0);
            encoder.SetVertexBuffer(uniformBuffer, 0, 1);
            encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (ulong)drawCount);
            return drawCount;
        }

        public void Dispose() { }

        // ── Private ───────────────────────────────────────────────────────────────

        private unsafe void UploadToGpu(PointData[] points)
        {
            if (points.Length == 0) return;

            var device = MetalFrameContext.Device;
            ulong byteSize = (ulong)(points.Length * PointStride);

            // Keep a single managed vertex buffer. The previous staging+private copy
            // doubled point-cloud VRAM pressure and could trigger black frames under load.
            _pointBuffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            fixed (PointData* src = points)
                Buffer.MemoryCopy(src, _pointBuffer.Contents.ToPointer(), byteSize, byteSize);
            _pointBuffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });
        }

        [System.Runtime.InteropServices.DllImport("libobjc.dylib", EntryPoint = "objc_release")]
        private static extern void NativeRelease(IntPtr obj);
    }
}
