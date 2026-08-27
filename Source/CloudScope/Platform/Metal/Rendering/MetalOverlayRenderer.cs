using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using CloudScope.Rendering;
using CloudScope.Selection;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalOverlayRenderer : IOverlayRenderer
    {

        private readonly MetalRenderContext _context;

        public MetalOverlayRenderer(MetalRenderContext context)
        {
            _context = context;
            _renderer = new MetalPrimitiveRenderer(context);
        }
        private readonly MetalPrimitiveRenderer _renderer;
        private MTLBuffer _crosshairBuffer;
        private MTLBuffer _modeBuffer;
        private MTLBuffer _viewportBorderBuffer;
        private MTLBuffer[] _pivotBuffers = Array.Empty<MTLBuffer>();
        private PivotLineBatch[] _pivotBatches = Array.Empty<PivotLineBatch>();
        private readonly float[] _crosshairVertices = new float[12];
        private readonly float[] _modeVertices = new float[12];
        private readonly float[] _viewportBorderVertices = new float[15];
        private MTLRenderPipelineState _pivotPointPipeline;
        private MTLDepthStencilState _pivotPointDepthState;
        private MTLBuffer _pivotPointBuffer;
        private MTLBuffer _pivotPointUniforms;

        /// <summary>Pixel width of the selection-mode indicator cross; matches the OpenGL overlay.</summary>
        private const float ModeIndicatorWidth = 2.5f;

        public void Initialize()
        {
            _renderer.EnsureResources();
            _pivotBatches = PivotIndicatorGeometry.BuildBatches();
            _pivotBuffers = new MTLBuffer[_pivotBatches.Length];
            for (int i = 0; i < _pivotBatches.Length; i++)
            {
                PivotLineBatch batch = _pivotBatches[i];
                float[] vertices = batch.IsClosedLoop
                    ? PivotIndicatorGeometry.BuildSmoothLoopVertices(batch.Positions)
                    : batch.Positions;
                _pivotBuffers[i] = _renderer.CreateStaticBuffer(vertices);
            }
            var device = _context.Device;
            _pivotPointPipeline = MetalShaderLibrary.CreatePivotPointPipeline(
                device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float, _context.SampleCount);
            _pivotPointDepthState = MetalShaderLibrary.CreateDepthState(device, depthWrite: false);
            _pivotPointBuffer = CreatePivotPointBuffer();
            _pivotPointUniforms = device.NewBuffer(
                (ulong)Unsafe.SizeOf<MetalPointUniforms>(), MTLResourceOptions.ResourceStorageModeShared);
        }

        public void RenderPivotIndicator(
            IRenderFrameData frameData,
            ref Matrix4 view, ref Matrix4 proj,
            OrbitCamera camera, Vector3 pivot, float fade, float flash)
        {
            if (frameData is not MetalFrameState frame) return;
            _renderer.SetFrame(frame);
            float alpha = System.Math.Clamp(fade + flash, 0f, 1f);
            if (alpha <= 0.01f) return;

            float  scale = camera.PivotIndicatorScaleAt(pivot);
            Matrix4 model = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(pivot);
            Matrix4 mvp   = model * view * proj;
            for (int i = 0; i < _pivotBatches.Length; i++)
            {
                PivotLineBatch batch = _pivotBatches[i];
                Vector4 color = new(batch.Color, alpha);
                if (batch.IsClosedLoop)
                {
                    int vertexCount = (batch.PointCount + 1) * 2;
                    _renderer.DrawSmoothPolyline(_pivotBuffers[i], vertexCount, mvp, color,
                        depthTest: true, lineWidthPixels: 1f + alpha);
                    color.W = alpha * 0.20f;
                    _renderer.DrawSmoothPolyline(_pivotBuffers[i], vertexCount, mvp, color,
                        depthTest: true, lineWidthPixels: LineWidth.NativeMax, occludedOnly: true);
                }
                else
                {
                    _renderer.Draw(_pivotBuffers[i], batch.PointCount, MTLPrimitiveType.Line, mvp, color,
                        depthTest: true, lineWidthPixels: 1f + alpha);
                    color.W = alpha * 0.20f;
                    _renderer.Draw(_pivotBuffers[i], batch.PointCount, MTLPrimitiveType.Line, mvp, color,
                        depthTest: false, lineWidthPixels: LineWidth.NativeMax);
                }
            }
            RenderPivotPoint(frame, ref view, ref proj, pivot, 11f + 11f * fade + flash * 14f, alpha);
        }

        public void RenderCenterCrosshair(IRenderFrameData frameData, int width, int height, float alpha)
        {
            if (frameData is not MetalFrameState frame) return;
            _renderer.SetFrame(frame);
            Vector2 extent = OverlayLayout.CrosshairExtent(width, height);
            FillCrosshairVertices(_crosshairVertices, Vector2.Zero, extent);
            _renderer.UpdateBuffer(ref _crosshairBuffer, _crosshairVertices);
            Matrix4 shadow = Matrix4.CreateTranslation(1f / width, -1f / height, 0f);
            _renderer.Draw(_crosshairBuffer, 4, MTLPrimitiveType.Line,
                shadow, new Vector4(0f, 0f, 0f, alpha * 0.55f), depthTest: false);
            _renderer.Draw(_crosshairBuffer, 4, MTLPrimitiveType.Line,
                Matrix4.Identity, new Vector4(0.55f, 0.55f, 0.55f, alpha), depthTest: false);
        }

        public void RenderModeIndicator(IRenderFrameData frameData, int width, int height, SelectionToolType toolType)
        {
            if (frameData is not MetalFrameState frame) return;
            _renderer.SetFrame(frame);
            (Vector2 center, Vector2 extent) = OverlayLayout.ModeIndicator(width, height);
            FillCrosshairVertices(_modeVertices, center, extent);
            _renderer.UpdateBuffer(ref _modeBuffer, _modeVertices);
            Vector3 rgb = OverlayLayout.ModeColor(toolType);
            Vector4 color = new(rgb, 0.9f);
            _renderer.Draw(_modeBuffer, 4, MTLPrimitiveType.Line,
                Matrix4.Identity, color, depthTest: false, lineWidthPixels: ModeIndicatorWidth);
        }

        public void RenderViewportBorder(IRenderFrameData frameData, int width, int height, bool active)
        {
            if (frameData is not MetalFrameState frame) return;
            _renderer.SetFrame(frame);
            float insetX = 1f / System.Math.Max(width, 1);
            float insetY = 1f / System.Math.Max(height, 1);
            float left = -1f + insetX, right = 1f - insetX;
            float bottom = -1f + insetY, top = 1f - insetY;
            WriteBorderVertex(_viewportBorderVertices, 0, left, top);
            WriteBorderVertex(_viewportBorderVertices, 1, right, top);
            WriteBorderVertex(_viewportBorderVertices, 2, right, bottom);
            WriteBorderVertex(_viewportBorderVertices, 3, left, bottom);
            WriteBorderVertex(_viewportBorderVertices, 4, left, top);
            _renderer.UpdateBuffer(ref _viewportBorderBuffer, _viewportBorderVertices);
            Vector4 color = active
                ? new Vector4(0.10f, 0.72f, 1f, 1f)
                : new Vector4(0.34f, 0.37f, 0.41f, 0.9f);
            _renderer.Draw(_viewportBorderBuffer, 5, MTLPrimitiveType.LineStrip,
                Matrix4.Identity, color, depthTest: false);
        }

        public void Dispose()
        {
            MetalResources.Release(ref _crosshairBuffer);
            MetalResources.Release(ref _modeBuffer);
            MetalResources.Release(ref _viewportBorderBuffer);
            for (int i = 0; i < _pivotBuffers.Length; i++)
                MetalResources.Release(ref _pivotBuffers[i]);
            _pivotBuffers = Array.Empty<MTLBuffer>();
            _pivotBatches = Array.Empty<PivotLineBatch>();
            MetalResources.Release(ref _pivotPointBuffer);
            MetalResources.Release(ref _pivotPointUniforms);
            MetalResources.Release(_pivotPointPipeline.NativePtr);
            MetalResources.Release(_pivotPointDepthState.NativePtr);
            _pivotPointPipeline = default;
            _pivotPointDepthState = default;
            _renderer.Dispose();
        }

        private static void FillCrosshairVertices(float[] vertices, Vector2 center, Vector2 extent)
        {
            vertices[0] = center.X - extent.X; vertices[1] = center.Y; vertices[2] = 0f;
            vertices[3] = center.X + extent.X; vertices[4] = center.Y; vertices[5] = 0f;
            vertices[6] = center.X; vertices[7] = center.Y - extent.Y; vertices[8] = 0f;
            vertices[9] = center.X; vertices[10] = center.Y + extent.Y; vertices[11] = 0f;
        }

        private static void WriteBorderVertex(float[] vertices, int vertex, float x, float y)
        {
            int i = vertex * 3;
            vertices[i] = x;
            vertices[i + 1] = y;
            vertices[i + 2] = 0f;
        }

        private unsafe MTLBuffer CreatePivotPointBuffer()
        {
            var point = new PointData { R = 1f, G = 0.92f, B = 0.2f };
            ulong byteSize = (ulong)Unsafe.SizeOf<PointData>();
            MTLBuffer buffer = _context.Device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            Buffer.MemoryCopy(&point, buffer.Contents.ToPointer(), byteSize, byteSize);
            buffer.DidModifyRange(new SharpMetal.Foundation.NSRange { location = 0, length = byteSize });
            return buffer;
        }

        private void RenderPivotPoint(
            MetalFrameState frame,
            ref Matrix4 view,
            ref Matrix4 projection,
            Vector3 pivot,
            float pointSize,
            float alpha)
        {
            var encoder = frame.RenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero || _pivotPointPipeline.NativePtr == IntPtr.Zero)
                return;

            Matrix4 pointView = Matrix4.CreateTranslation(pivot) * view;
            MetalBufferWriter.Write(_pivotPointUniforms, new MetalPointUniforms(pointView, projection, pointSize, alpha));
            encoder.SetRenderPipelineState(_pivotPointPipeline);
            encoder.SetDepthStencilState(_pivotPointDepthState);
            encoder.SetVertexBuffer(_pivotPointBuffer, 0, 0);
            encoder.SetVertexBuffer(_pivotPointUniforms, 0, 1);
            encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, 1);
        }
    }
}
