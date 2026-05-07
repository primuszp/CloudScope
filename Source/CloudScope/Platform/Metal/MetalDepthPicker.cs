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
            
            // OrbitCamera passes glY (bottom-up), but Metal textures are top-down.
            // We flip it back to get the correct texture row.
            int correctedY = texH - 1 - y;

            int startX = Math.Clamp(x, 0, texW);
            int startY = Math.Clamp(correctedY, 0, texH);
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

            CopyDepthToDestination(texture, startX, startY, readW, readH, destination);

            return count;
        }

        private static unsafe void CopyDepthToDestination(
            MTLTexture texture,
            int startX,
            int startY,
            int readW,
            int readH,
            Span<float> destination)
        {
            var queue = MetalFrameContext.CommandQueue;
            if (queue.NativePtr == IntPtr.Zero)
                return;

            ulong bytesPerRow = Align256((ulong)(readW * sizeof(float)));
            ulong byteSize = bytesPerRow * (ulong)readH;
            var readback = MetalFrameContext.Device.NewBuffer(
                byteSize,
                MTLResourceOptions.ResourceStorageModeShared);
            if (readback.NativePtr == IntPtr.Zero)
                return;

            var commandBuffer = queue.CommandBuffer();
            var blit = commandBuffer.BlitCommandEncoder();
            blit.CopyFromTexture(
                texture,
                0,
                0,
                new MTLOrigin { x = (ulong)startX, y = (ulong)startY, z = 0 },
                new MTLSize { width = (ulong)readW, height = (ulong)readH, depth = 1 },
                readback,
                0,
                bytesPerRow,
                bytesPerRow * (ulong)readH);
            blit.EndEncoding();
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();

            byte* srcBase = (byte*)readback.Contents.ToPointer();
            fixed (float* dstBase = destination)
            {
                for (int row = 0; row < readH; row++)
                {
                    float* src = (float*)(srcBase + (ulong)row * bytesPerRow);
                    float* dst = dstBase + row * readW;
                    for (int col = 0; col < readW; col++)
                        dst[col] = src[col];
                }
            }
        }

        private static ulong Align256(ulong value) => (value + 255UL) & ~255UL;
    }
}
