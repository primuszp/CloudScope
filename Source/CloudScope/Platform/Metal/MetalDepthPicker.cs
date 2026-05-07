using System;
using System.Runtime.Versioning;
using CloudScope.Rendering;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalDepthPicker : IDepthPicker
    {
        public float ReadDepth(int x, int y)
        {
            Span<float> one = stackalloc float[1];
            one[0] = 1f;
            ReadDepthWindow(x, y, 1, 1, one);
            return one[0];
        }

        public int ReadDepthWindow(int x, int y, int width, int height, float[] destination)
        {
            if (destination.Length == 0)
                return 0;

            return ReadDepthWindow(x, y, width, height, destination.AsSpan());
        }

        private unsafe int ReadDepthWindow(int x, int y, int width, int height, Span<float> destination)
        {
            var texture = MetalFrameContext.DepthTexture;
            if (texture.NativePtr == IntPtr.Zero || width <= 0 || height <= 0)
            {
                destination.Fill(1f);
                return 0;
            }

            int texW = checked((int)texture.Width);
            int texH = checked((int)texture.Height);
            int startX = Math.Clamp(x, 0, texW);
            int startY = Math.Clamp(y, 0, texH);
            int readW = Math.Min(width, texW - startX);
            int readH = Math.Min(height, texH - startY);
            if (readW > 0 && readH > 0 && destination.Length < readW * readH)
                readH = Math.Max(0, destination.Length / readW);

            int count = Math.Max(0, readW * readH);
            if (count == 0)
            {
                destination.Fill(1f);
                return 0;
            }

            SynchronizeDepthTexture(texture);

            fixed (float* dst = destination)
            {
                texture.GetBytes(
                    (IntPtr)dst,
                    (ulong)(readW * sizeof(float)),
                    new MTLRegion
                    {
                        origin = new MTLOrigin { x = (ulong)startX, y = (ulong)startY, z = 0 },
                        size = new MTLSize { width = (ulong)readW, height = (ulong)readH, depth = 1 }
                    },
                    0);
            }

            return count;
        }

        private static void SynchronizeDepthTexture(MTLTexture texture)
        {
            var queue = MetalFrameContext.CommandQueue;
            if (queue.NativePtr == IntPtr.Zero)
                return;

            var commandBuffer = queue.CommandBuffer();
            var blit = commandBuffer.BlitCommandEncoder();
            blit.SynchronizeTexture(texture, 0, 0);
            blit.EndEncoding();
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();
        }
    }
}
