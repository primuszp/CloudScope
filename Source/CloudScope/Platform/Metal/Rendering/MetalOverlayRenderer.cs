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

        public void Initialize()
        {
            _renderer.EnsureResources();
            _pivotBuffer = _renderer.CreateStaticBuffer(BuildPivotGeometry());
        }

        public void RenderPivotIndicator(
            ref Matrix4 view, ref Matrix4 proj,
            OrbitCamera camera, Vector3 pivot, float fade, float flash)
        {
            float alpha = System.Math.Clamp(fade + flash, 0f, 1f);
            if (alpha <= 0.01f) return;

            float  scale = camera.PivotIndicatorScaleAt(pivot);
            Matrix4 model = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(pivot);
            Matrix4 mvp   = model * view * proj;
            _renderer.Draw(_pivotBuffer, 6, MTLPrimitiveType.Line, mvp,
                new Vector4(1f, 0.85f, 0.15f, alpha), depthTest: false);
        }

        public void RenderCenterCrosshair(int width, int height, float alpha)
        {
            float sx = 15f / width;
            float sy = 15f / height;
            _renderer.UpdateBuffer(ref _crosshairBuffer, new[]
            {
                -sx, 0f, 0f,  sx, 0f, 0f,
                 0f, -sy, 0f, 0f, sy, 0f,
            });
            _renderer.Draw(_crosshairBuffer, 4, MTLPrimitiveType.Line,
                Matrix4.Identity, new Vector4(0.55f, 0.55f, 0.55f, alpha), depthTest: false);
        }

        public void RenderModeIndicator(int width, int height, SelectionToolType toolType)
        {
            float x  = -1f + 30f / width;
            float y  =  1f - 30f / height;
            float sx = 8f / width;
            float sy = 8f / height;
            _renderer.UpdateBuffer(ref _modeBuffer, new[]
            {
                x - sx, y, 0f,  x + sx, y, 0f,
                x, y - sy, 0f,  x, y + sy, 0f,
            });
            Vector4 color = toolType switch
            {
                SelectionToolType.Sphere   => new Vector4(1f,    0.6f,  0.15f, 0.9f),
                SelectionToolType.Cylinder => new Vector4(0.60f, 0.25f, 1f,    0.9f),
                _                          => new Vector4(0f,    0.8f,  1f,    0.9f),
            };
            _renderer.Draw(_modeBuffer, 4, MTLPrimitiveType.Line,
                Matrix4.Identity, color, depthTest: false);
        }

        public void Dispose() => _renderer.Dispose();

        private static float[] BuildPivotGeometry() => new[]
        {
            -0.55f, 0f, 0f,  0.55f, 0f, 0f,
             0f, -0.55f, 0f, 0f,  0.55f, 0f,
             0f, 0f, -0.55f, 0f,  0f,  0.55f,
        };
    }
}
