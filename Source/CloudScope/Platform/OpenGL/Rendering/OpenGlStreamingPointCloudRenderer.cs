using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using CloudScope.Loading;
using CloudScope.Rendering;
using CloudScope.Store;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Draws an on-disk cloud by keeping the visible cells in a fixed-size vertex buffer.
    /// </summary>
    /// <remarks>
    /// The buffer is allocated once at its full budget and never resized; cells are written
    /// into pages of it as the camera moves. That is the whole difference from
    /// <see cref="OpenGlPointCloudRenderer"/>: same shader, same vertex layout, same
    /// <c>MultiDrawArrays</c>, but the ranges name pages of a working set rather than parts of
    /// a cloud that had to fit in memory first.
    /// </remarks>
    internal sealed class OpenGlStreamingPointCloudRenderer : IPointTileCloudRenderer, IPointTilePageBuffer
    {
        /// <summary>
        /// Points per page — a megabyte of vertices.
        /// </summary>
        /// <remarks>
        /// Small enough that the tail wasted by a cell that does not fill its last page stays
        /// under a percent of the budget at realistic cell sizes, large enough that a frame's
        /// draw range list stays in the thousands rather than the hundreds of thousands.
        /// </remarks>
        private const int PageSizeInPoints = 64 * 1024;

        private static PointRenderLimits Limits => PointRenderLimits.For("OPENGL");

        private readonly OpenGlPointCloudProgram _program = new();

        private IReadOnlyList<PointTileLayer> _layers = Array.Empty<PointTileLayer>();
        private PointTileResidency? _residency;
        private PointTilePageTable? _pages;

        private int _vao = -1, _vbo = -1, _attributeVbo = -1;
        private bool _hasAttributes;
        private ColorSource _colorSource = ColorSource.Rgb;

        private PointTileVisit[] _visits = Array.Empty<PointTileVisit>();
        private PointDrawRange[] _ranges = Array.Empty<PointDrawRange>();
        private PointTileLayerBatch[] _batches = Array.Empty<PointTileLayerBatch>();
        private int[] _multiDrawFirst = Array.Empty<int>();
        private int[] _multiDrawCount = Array.Empty<int>();
        private readonly PointTileTraversalScratch _scratch = new();

        public long DrawnPointCount { get; private set; }

        public int ResidentCellCount => _residency?.ResidentCellCount ?? 0;

        public int PendingCellCount => _residency?.PendingCellCount ?? 0;

        public void Initialize() => _program.Initialize();

        public void Open(IReadOnlyList<PointTileLayer> layers)
        {
            Release();
            if (layers.Count == 0)
                return;

            _layers = layers;
            // Every layer's store must agree on whether attributes exist, since they share one
            // buffer and one vertex layout. A layer without them falls back to stored color.
            _hasAttributes = layers.All(layer => layer.Store.Header.HasAttributes);

            long totalPoints = layers.Sum(layer => layer.Store.Header.PointCount);
            int bytesPerPoint = GpuPointVertex.Stride + (_hasAttributes ? GpuPointAttribute.Stride : 0);
            // OpenGL has no portable way to ask how much memory the device has, so the working
            // set is bounded by CLOUDSCOPE_MAX_RESIDENT_POINTS, or by the clouds themselves
            // when together they are smaller than the default ceiling.
            long residentPoints = Math.Min(totalPoints, Limits.ResolveResidentLimit(null, bytesPerPoint));
            int pageCount = (int)Math.Max(1, residentPoints / PageSizeInPoints);
            _pages = new PointTilePageTable(PageSizeInPoints, pageCount);

            int cellCount = layers.Sum(layer => layer.Store.Nodes.Length);
            _visits = new PointTileVisit[cellCount];
            // A cell contributes one range per page it occupies, and the budget bounds how
            // many pages can be resident at once.
            _ranges = new PointDrawRange[pageCount + cellCount];
            _batches = new PointTileLayerBatch[layers.Count];
            _multiDrawFirst = new int[_ranges.Length];
            _multiDrawCount = new int[_ranges.Length];

            long capacity = _pages.CapacityInPoints;
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(capacity * GpuPointVertex.Stride),
                IntPtr.Zero,
                BufferUsageHint.DynamicDraw);
            OpenGlPointCloudProgram.BindPositionAttributes();

            if (_hasAttributes)
            {
                _attributeVbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, _attributeVbo);
                GL.BufferData(
                    BufferTarget.ArrayBuffer,
                    (IntPtr)(capacity * GpuPointAttribute.Stride),
                    IntPtr.Zero,
                    BufferUsageHint.DynamicDraw);
                OpenGlPointCloudProgram.BindAttributeAttributes();
            }

            GL.BindVertexArray(0);

            _residency = new PointTileResidency(layers, this, _pages);
        }

        public void Close() => Release();

        public void UpdateColorSource(ColorSource source) => _colorSource = source;

        /// <summary>Writes one page of a cell into the resident buffers.</summary>
        public void WritePage(
            int firstPoint,
            ReadOnlySpan<GpuPointVertex> points,
            ReadOnlySpan<GpuPointAttribute> attributes)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferSubData(
                BufferTarget.ArrayBuffer,
                (IntPtr)((long)firstPoint * GpuPointVertex.Stride),
                points.Length * GpuPointVertex.Stride,
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(points));

            if (_attributeVbo == -1 || attributes.IsEmpty)
                return;

            GL.BindBuffer(BufferTarget.ArrayBuffer, _attributeVbo);
            GL.BufferSubData(
                BufferTarget.ArrayBuffer,
                (IntPtr)((long)firstPoint * GpuPointAttribute.Stride),
                attributes.Length * GpuPointAttribute.Stride,
                ref System.Runtime.InteropServices.MemoryMarshal.GetReference(attributes));
        }

        public int Render(IRenderFrameData frameData, in PointRenderView renderView)
        {
            DrawnPointCount = 0;
            if (_residency is null || _pages is null || _layers.Count == 0)
                return 0;

            long totalPoints = 0;
            foreach (PointTileLayer layer in _layers)
                totalPoints += layer.Store.Header.PointCount;

            int drawBudget = Limits.GetFrameBudget((int)Math.Min(totalPoints, int.MaxValue));
            int visitCount = PointTileTraversal.Collect(
                _layers, in renderView, drawBudget, _visits, _scratch, out _);

            // Uploads happen inside BeginFrame, so it runs even when nothing is visible: that
            // is what lets a cloud finish streaming in while the camera sits still.
            int batchCount = _residency.BeginFrame(
                _visits.AsSpan(0, visitCount), _ranges, _batches, out long drawnPointCount);
            DrawnPointCount = drawnPointCount;
            if (batchCount == 0)
                return 0;

            _program.Use(in renderView, _colorSource, _hasAttributes);
            GL.BindVertexArray(_vao);

            for (int batch = 0; batch < batchCount; batch++)
            {
                PointTileLayerBatch layerBatch = _batches[batch];
                for (int i = 0; i < layerBatch.RangeCount; i++)
                {
                    PointDrawRange range = _ranges[layerBatch.FirstRange + i];
                    _multiDrawFirst[i] = range.First;
                    _multiDrawCount[i] = range.Count;
                }

                // One MultiDrawArrays per layer instead of one per cell: a large cloud plans a
                // couple of thousand ranges every frame, and that many driver round trips shows
                // up in the frame time on its own.
                _program.SetLayerTint(_layers[layerBatch.LayerIndex].Tint);
                GL.MultiDrawArrays(
                    PrimitiveType.Points, _multiDrawFirst, _multiDrawCount, layerBatch.RangeCount);
            }

            return (int)Math.Min(drawnPointCount, int.MaxValue);
        }

        public void Dispose()
        {
            Release();
            _program.Dispose();
        }

        private void Release()
        {
            // The residency frees its pages against the table, so it goes first.
            _residency?.Dispose();
            _residency = null;
            _pages = null;
            _layers = Array.Empty<PointTileLayer>();

            if (_vbo != -1)
            {
                GL.DeleteBuffer(_vbo);
                _vbo = -1;
            }

            if (_attributeVbo != -1)
            {
                GL.DeleteBuffer(_attributeVbo);
                _attributeVbo = -1;
            }

            if (_vao != -1)
            {
                GL.DeleteVertexArray(_vao);
                _vao = -1;
            }

            _visits = Array.Empty<PointTileVisit>();
            _ranges = Array.Empty<PointDrawRange>();
            _batches = Array.Empty<PointTileLayerBatch>();
            _multiDrawFirst = Array.Empty<int>();
            _multiDrawCount = Array.Empty<int>();
        }
    }
}
