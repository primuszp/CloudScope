using OpenTK.Graphics.OpenGL4;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL
{
    public sealed class OpenGlDepthPicker : IDepthPicker
    {
        public float ReadDepth(int x, int y)
        {
            float depth = 1f;
            GL.ReadPixels(x, y, 1, 1, PixelFormat.DepthComponent, PixelType.Float, ref depth);
            return depth;
        }

        public int ReadDepthWindow(int x, int y, int width, int height, float[] destination)
        {
            GL.ReadPixels(x, y, width, height, PixelFormat.DepthComponent, PixelType.Float, destination);
            return width * height;
        }
    }
}
