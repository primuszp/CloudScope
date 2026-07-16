using System.Runtime.Versioning;
using CloudScope.Rendering;
using CloudScope.Selection;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalOverlayRenderer : IOverlayRenderer
    {
        private readonly MetalPrimitiveRenderer _renderer = new();
        private MTLBuffer _crosshairBuffer;
        private MTLBuffer _modeBuffer;
        private MTLBuffer _pivotBuffer;
        private readonly float[] _crosshairVertices = new float[12];
        private readonly float[] _modeVertices = new float[12];

        public void Initialize()
        {
            _renderer.EnsureResources();
            _pivotBuffer = _renderer.CreateStaticBuffer(BuildPivotGeometry());
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
            _renderer.Draw(_pivotBuffer, 6, MTLPrimitiveType.Line, mvp,
                new Vector4(1f, 0.85f, 0.15f, alpha), depthTest: false);
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
                Matrix4.Identity, color, depthTest: false);
        }

        public void Dispose()
        {
            MetalPrimitiveRenderer.Release(ref _crosshairBuffer);
            MetalPrimitiveRenderer.Release(ref _modeBuffer);
            MetalPrimitiveRenderer.Release(ref _pivotBuffer);
            _renderer.Dispose();
        }

        private static float[] BuildPivotGeometry() => new[]
        {
            -0.55f, 0f, 0f,  0.55f, 0f, 0f,
             0f, -0.55f, 0f, 0f,  0.55f, 0f,
             0f, 0f, -0.55f, 0f,  0f,  0.55f,
        };

        private static void FillCrosshairVertices(float[] vertices, Vector2 center, Vector2 extent)
        {
            vertices[0] = center.X - extent.X; vertices[1] = center.Y; vertices[2] = 0f;
            vertices[3] = center.X + extent.X; vertices[4] = center.Y; vertices[5] = 0f;
            vertices[6] = center.X; vertices[7] = center.Y - extent.Y; vertices[8] = 0f;
            vertices[9] = center.X; vertices[10] = center.Y + extent.Y; vertices[11] = 0f;
        }
    }
}
