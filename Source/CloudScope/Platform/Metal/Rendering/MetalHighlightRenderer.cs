#if MACOS
using System;
using System.Collections.Generic;
using CloudScope.Labeling;
using CloudScope.Rendering;
using Foundation;
using Metal;
using OpenTK.Mathematics;

namespace CloudScope.Platform.Metal
{
    internal sealed class MetalHighlightRenderer : IHighlightRenderer
    {
        private static readonly Dictionary<string, Vector3> Palette = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ground"] = new Vector3(0.55f, 0.27f, 0.07f),
            ["Building"] = new Vector3(1.0f, 0.27f, 0.27f),
            ["Vegetation"] = new Vector3(0.13f, 0.80f, 0.13f),
            ["Vehicle"] = new Vector3(1.0f, 0.84f, 0.0f),
            ["Road"] = new Vector3(0.60f, 0.60f, 0.60f),
            ["Water"] = new Vector3(0.12f, 0.56f, 1.0f),
            ["Wire"] = new Vector3(0.93f, 0.51f, 0.93f),
        };

        private IMTLRenderPipelineState? pipeline;
        private IMTLDepthStencilState? depthState;
        private IMTLBuffer? highlightBuffer;
        private IMTLBuffer? previewBuffer;
        private IMTLBuffer? uniformsBuffer;
        private bool dirty = true;
        private int highlightCount;
        private int previewCount;
        private PointData[] vertexScratch = Array.Empty<PointData>();

        public void MarkDirty() => dirty = true;

        public void UpdatePreview(PointData[]? points, IReadOnlyList<int>? indices)
        {
            EnsureResources();
            if (points == null || indices == null || indices.Count == 0)
            {
                previewCount = 0;
                return;
            }

            var data = RentScratch(indices.Count);
            int count = 0;
            foreach (int i in indices)
            {
                if ((uint)i >= (uint)points.Length)
                    continue;
                data[count] = points[i];
                data[count].R = 1.0f;
                data[count].G = 0.85f;
                data[count].B = 0.1f;
                count++;
            }

            previewCount = count;
            previewBuffer = CreateBuffer(data, count);
        }

        public void RenderPreview(ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            RenderBuffer(previewBuffer, previewCount, ref view, ref proj, pointSize + 2f);
        }

        public void Render(PointData[] points, LabelManager labels, ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            EnsureResources();
            if (dirty)
            {
                RebuildBuffer(points, labels);
                dirty = false;
            }

            RenderBuffer(highlightBuffer, highlightCount, ref view, ref proj, pointSize + 2f);
        }

        public void Dispose()
        {
            highlightBuffer?.Dispose();
            previewBuffer?.Dispose();
            uniformsBuffer?.Dispose();
            pipeline?.Dispose();
            depthState?.Dispose();
        }

        private void EnsureResources()
        {
            if (pipeline != null)
                return;

            var device = MetalFrameContext.CurrentView?.Device ?? MTLDevice.SystemDefault
                ?? throw new PlatformNotSupportedException("No Metal device is available.");
            pipeline = MetalShaderLibrary.CreatePointPipeline(
                device,
                MetalFrameContext.CurrentView?.ColorPixelFormat ?? MTLPixelFormat.BGRA8Unorm,
                MetalFrameContext.CurrentView?.DepthStencilPixelFormat ?? MTLPixelFormat.Depth32Float);
            depthState = MetalShaderLibrary.CreateDepthState(device, depthWriteEnabled: false);
            uniformsBuffer = device.CreateBuffer((UIntPtr)System.Runtime.CompilerServices.Unsafe.SizeOf<MetalPointUniforms>(), MTLResourceOptions.CpuCacheModeDefault);
        }

        private void RebuildBuffer(PointData[] points, LabelManager labels)
        {
            var allLabels = labels.AllLabels;
            var data = RentScratch(allLabels.Count);
            int count = 0;

            foreach (var (ptIdx, labelName) in allLabels)
            {
                if ((uint)ptIdx >= (uint)points.Length)
                    continue;
                data[count] = points[ptIdx];
                var color = GetLabelColor(labelName);
                data[count].R = color.X;
                data[count].G = color.Y;
                data[count].B = color.Z;
                count++;
            }

            highlightCount = count;
            highlightBuffer = CreateBuffer(data, count);
        }

        private void RenderBuffer(IMTLBuffer? buffer, int count, ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            if (count == 0 || buffer == null || pipeline == null || uniformsBuffer == null)
                return;

            var commandBuffer = MetalFrameContext.CurrentCommandBuffer;
            var descriptor = MetalFrameContext.CurrentView?.CurrentRenderPassDescriptor;
            if (commandBuffer == null || descriptor == null)
                return;

            descriptor.ColorAttachments[0].LoadAction = MTLLoadAction.Load;
            MetalBufferWriter.Write(uniformsBuffer, new MetalPointUniforms(view, proj, pointSize));

            using var encoder = commandBuffer.CreateRenderCommandEncoder(descriptor);
            encoder.SetRenderPipelineState(pipeline);
            if (depthState != null)
                encoder.SetDepthStencilState(depthState);
            encoder.SetVertexBuffer(buffer, UIntPtr.Zero, 0);
            encoder.SetVertexBuffer(uniformsBuffer, UIntPtr.Zero, 1);
            encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (UIntPtr)count);
            encoder.EndEncoding();
        }

        private unsafe IMTLBuffer? CreateBuffer(PointData[] data, int count)
        {
            if (count == 0)
                return null;

            var device = MetalFrameContext.CurrentView?.Device ?? MTLDevice.SystemDefault
                ?? throw new PlatformNotSupportedException("No Metal device is available.");
            fixed (PointData* ptr = data)
            {
                return device.CreateBuffer((IntPtr)ptr, (UIntPtr)(count * 24), MTLResourceOptions.StorageModeManaged);
            }
        }

        private PointData[] RentScratch(int count)
        {
            if (vertexScratch.Length < count)
                vertexScratch = new PointData[count];
            return vertexScratch;
        }

        private static Vector3 GetLabelColor(string label)
        {
            if (Palette.TryGetValue(label, out var color))
                return color;

            int h = label.GetHashCode();
            float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
            float s = 0.75f, v = 0.9f;
            int hi = (int)(hue * 6f) % 6;
            float f = hue * 6f - hi;
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            return hi switch
            {
                0 => new Vector3(v, t, p),
                1 => new Vector3(q, v, p),
                2 => new Vector3(p, v, t),
                3 => new Vector3(p, q, v),
                4 => new Vector3(t, p, v),
                _ => new Vector3(v, p, q),
            };
        }
    }
}
#endif
