using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CloudScope.Labeling;
using CloudScope.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalHighlightRenderer : IHighlightRenderer
    {
        private static readonly Dictionary<string, Vector3> Palette = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ground"]     = new Vector3(0.55f, 0.27f, 0.07f),
            ["Building"]   = new Vector3(1.0f,  0.27f, 0.27f),
            ["Vegetation"] = new Vector3(0.13f, 0.80f, 0.13f),
            ["Vehicle"]    = new Vector3(1.0f,  0.84f, 0.0f),
            ["Road"]       = new Vector3(0.60f, 0.60f, 0.60f),
            ["Water"]      = new Vector3(0.12f, 0.56f, 1.0f),
            ["Wire"]       = new Vector3(0.93f, 0.51f, 0.93f),
        };

        private MTLRenderPipelineState _pipeline;
        private MTLDepthStencilState _depthState;
        private MTLBuffer _uniformsBuffer;
        private MTLBuffer _highlightBuffer;
        private MTLBuffer _previewBuffer;
        private bool _dirty = true;
        private int _highlightCount;
        private int _previewCount;
        private PointData[] _scratch = Array.Empty<PointData>();

        public void MarkDirty() => _dirty = true;

        public void UpdatePreview(PointData[]? points, IReadOnlyList<int>? indices)
        {
            EnsureResources();
            if (points == null || indices == null || indices.Count == 0)
            {
                _previewCount = 0;
                return;
            }

            var data = RentScratch(indices.Count);
            int count = 0;
            foreach (int i in indices)
            {
                if ((uint)i >= (uint)points.Length) continue;
                data[count] = points[i];
                data[count].R = 1.0f;
                data[count].G = 0.85f;
                data[count].B = 0.1f;
                count++;
            }
            _previewCount = count;
            BuildBuffer(ref _previewBuffer, data, count);
        }

        public void RenderPreview(ref Matrix4 view, ref Matrix4 proj, float pointSize)
            => RenderBuffer(_previewBuffer, _previewCount, ref view, ref proj, pointSize + 2f);

        public void Render(PointData[] points, LabelManager labels, ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            EnsureResources();
            if (_dirty)
            {
                RebuildHighlightBuffer(points, labels);
                _dirty = false;
            }
            RenderBuffer(_highlightBuffer, _highlightCount, ref view, ref proj, pointSize + 2f);
        }

        public void Dispose() { }

        // ── Private ───────────────────────────────────────────────────────────────

        private void EnsureResources()
        {
            if (_pipeline.NativePtr != IntPtr.Zero)
                return;

            var device = MetalFrameContext.Device;
            _pipeline    = MetalShaderLibrary.CreatePointPipeline(device, MTLPixelFormat.BGRA8Unorm, MTLPixelFormat.Depth32Float);
            _depthState  = MetalShaderLibrary.CreateDepthState(device, depthWrite: false);
            _uniformsBuffer = device.NewBuffer(
                (ulong)Unsafe.SizeOf<MetalPointUniforms>(),
                MTLResourceOptions.ResourceStorageModeShared);
        }

        private void RebuildHighlightBuffer(PointData[] points, LabelManager labels)
        {
            var allLabels = labels.AllLabels;
            var data  = RentScratch(allLabels.Count);
            int count = 0;
            foreach (var (ptIdx, labelName) in allLabels)
            {
                if ((uint)ptIdx >= (uint)points.Length) continue;
                data[count] = points[ptIdx];
                var c = GetLabelColor(labelName);
                data[count].R = c.X;
                data[count].G = c.Y;
                data[count].B = c.Z;
                count++;
            }
            _highlightCount  = count;
            BuildBuffer(ref _highlightBuffer, data, count);
        }

        private void RenderBuffer(MTLBuffer buffer, int count,
            ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            if (count == 0 || buffer.NativePtr == IntPtr.Zero || _pipeline.NativePtr == IntPtr.Zero)
                return;

            MetalBufferWriter.Write(_uniformsBuffer, new MetalPointUniforms(view, proj, pointSize));

            var encoder = MetalFrameContext.CurrentRenderCommandEncoder;
            if (encoder.NativePtr == IntPtr.Zero) return;

            encoder.SetRenderPipelineState(_pipeline);
            encoder.SetDepthStencilState(_depthState);
            encoder.SetVertexBuffer(buffer, 0, 0);
            encoder.SetVertexBuffer(_uniformsBuffer, 0, 1);
            encoder.DrawPrimitives(MTLPrimitiveType.Point, 0, (ulong)count);
        }

        private unsafe void BuildBuffer(ref MTLBuffer existingBuffer, PointData[] data, int count)
        {
            if (count == 0) return;
            ulong byteSize = (ulong)(count * 24);
            var device = MetalFrameContext.Device;

            if (existingBuffer.NativePtr == IntPtr.Zero || existingBuffer.Length < byteSize)
            {
                if (existingBuffer.NativePtr != IntPtr.Zero)
                    NativeRelease(existingBuffer.NativePtr);
                existingBuffer = device.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeManaged);
            }

            fixed (PointData* src = data)
                Buffer.MemoryCopy(src, existingBuffer.Contents.ToPointer(), byteSize, byteSize);
            existingBuffer.DidModifyRange(new NSRange { location = 0, length = byteSize });
        }

        private PointData[] RentScratch(int count)
        {
            if (_scratch.Length < count)
                _scratch = new PointData[count];
            return _scratch;
        }

        private static Vector3 GetLabelColor(string label)
        {
            if (Palette.TryGetValue(label, out var c)) return c;
            int h   = label.GetHashCode();
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
        [System.Runtime.InteropServices.DllImport("libobjc.dylib", EntryPoint = "objc_release")]
        private static extern void NativeRelease(IntPtr obj);
    }
}
