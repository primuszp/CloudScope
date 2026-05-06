#if MACOS
using CloudScope.Rendering;
using CloudScope.Selection;
using Metal;
using OpenTK.Mathematics;

namespace CloudScope.Platform.Metal
{
    internal sealed class MetalOverlayRenderer : IOverlayRenderer
    {
        private readonly MetalPrimitiveRenderer renderer = new();
        private IMTLBuffer? crosshairBuffer;
        private IMTLBuffer? modeBuffer;
        private IMTLBuffer? pivotBuffer;

        public void Initialize()
        {
            renderer.EnsureResources();
            pivotBuffer = renderer.CreateStaticBuffer(BuildPivotGeometry());
        }

        public void RenderPivotIndicator(ref Matrix4 view, ref Matrix4 proj, OrbitCamera camera, Vector3 pivot, float fade, float flash)
        {
            float alpha = MathHelper.Clamp(fade + flash, 0f, 1f);
            if (alpha <= 0.01f)
                return;

            float scale = camera.PivotIndicatorScaleAt(pivot);
            Matrix4 model = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(pivot);
            Matrix4 mvp = model * view * proj;
            renderer.Draw(pivotBuffer, 6, MTLPrimitiveType.Line, mvp, new Vector4(1f, 0.85f, 0.15f, alpha), depthTest: false);
        }

        public void RenderCenterCrosshair(int width, int height, float alpha)
        {
            float sx = 15f / width;
            float sy = 15f / height;
            crosshairBuffer?.Dispose();
            crosshairBuffer = renderer.CreateStaticBuffer(new[]
            {
                -sx, 0f, 0f, sx, 0f, 0f,
                0f, -sy, 0f, 0f, sy, 0f,
            });
            renderer.Draw(crosshairBuffer, 4, MTLPrimitiveType.Line, Matrix4.Identity, new Vector4(0.55f, 0.55f, 0.55f, alpha), depthTest: false);
        }

        public void RenderModeIndicator(int width, int height, SelectionToolType toolType)
        {
            float x = -1f + 30f / width;
            float y = 1f - 30f / height;
            float sx = 8f / width;
            float sy = 8f / height;
            modeBuffer?.Dispose();
            modeBuffer = renderer.CreateStaticBuffer(new[]
            {
                x - sx, y, 0f, x + sx, y, 0f,
                x, y - sy, 0f, x, y + sy, 0f,
            });
            Vector4 color = toolType switch
            {
                SelectionToolType.Sphere => new Vector4(1f, 0.6f, 0.15f, 0.9f),
                SelectionToolType.Cylinder => new Vector4(0.60f, 0.25f, 1f, 0.9f),
                _ => new Vector4(0f, 0.8f, 1f, 0.9f)
            };
            renderer.Draw(modeBuffer, 4, MTLPrimitiveType.Line, Matrix4.Identity, color, depthTest: false);
        }

        public void Dispose()
        {
            crosshairBuffer?.Dispose();
            modeBuffer?.Dispose();
            pivotBuffer?.Dispose();
            renderer.Dispose();
        }

        private static float[] BuildPivotGeometry() => new[]
        {
            -0.55f, 0f, 0f, 0.55f, 0f, 0f,
            0f, -0.55f, 0f, 0f, 0.55f, 0f,
            0f, 0f, -0.55f, 0f, 0f, 0.55f,
        };
    }
}
#endif
