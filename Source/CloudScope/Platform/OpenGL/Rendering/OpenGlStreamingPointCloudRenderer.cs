using System;
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

        private static readonly PointRenderLimits Limits = PointRenderLimits.Load("OPENGL");

        private readonly OpenGlPointCloudProgram _program = new();

        private PointTileStore? _store;
        private PointTileResidency? _residency;
        private PointTilePageTable? _pages;
        private int[] _roots = Array.Empty<int>();

        private int _vao = -1, _vbo = -1, _attributeVbo = -1;
        private bool _hasAttributes;
        private ColorSource _colorSource = ColorSource.Rgb;

        private PointTileVisit[] _visits = Array.Empty<PointTileVisit>();
        private PointDrawRange[] _ranges = Array.Empty<PointDrawRange>();
        private int[] _multiDrawFirst = Array.Empty<int>();
        private int[] _multiDrawCount = Array.Empty<int>();
        private readonly PointTileTraversalScratch _scratch = new();

        public long DrawnPointCount { get; private set; }

        public int ResidentCellCount => _residency?.ResidentCellCount ?? 0;

        public int PendingCellCount => _residency?.PendingCellCount ?? 0;

        public void Initialize() => _program.Initialize();

        public void Open(PointTileStore store)
        {
            Release();

            _store = store;
            _hasAttributes = store.Header.HasAttributes;
            _roots = PointTileTraversal.FindRoots(store.Nodes);

            int bytesPerPoint = GpuPointVertex.Stride + (_hasAttributes ? GpuPointAttribute.Stride : 0);
            // OpenGL has no portable way to ask how much memory the device has, so the working
            // set is bounded by CLOUDSCOPE_MAX_RESIDENT_POINTS, or by the cloud itself when it
            // is smaller than the default ceiling.
            long residentPoints = Math.Min(
                store.Header.PointCount,
                Limits.ResolveResidentLimit(null, bytesPerPoint));
            int pageCount = (int)Math.Max(1, residentPoints / PageSizeInPoints);
            _pages = new PointTilePageTable(PageSizeInPoints, pageCount);

            _visits = new PointTileVisit[store.Nodes.Length];
            // A cell contributes one range per page it occupies, and the budget bounds how
            // many pages can be resident at once.
            _ranges = new PointDrawRange[pageCount + _visits.Length];
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

            _residency = new PointTileResidency(store, this, _pages);
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
            if (_store is null || _residency is null || _pages is null)
                return 0;

            int drawBudget = Limits.GetFrameBudget((int)Math.Min(_store.Header.PointCount, int.MaxValue));
            int visitCount = PointTileTraversal.Collect(
                _store.Nodes, _roots, in renderView, drawBudget, _visits, _scratch, out _);

            // Uploads happen inside BeginFrame, so it runs even when nothing is visible: that
            // is what lets a cloud finish streaming in while the camera sits still.
            int rangeCount = _residency.BeginFrame(
                _visits.AsSpan(0, visitCount), _ranges, out long drawnPointCount);
            DrawnPointCount = drawnPointCount;
            if (rangeCount == 0)
                return 0;

            _program.Use(in renderView, _colorSource, _hasAttributes);
            GL.BindVertexArray(_vao);

            for (int i = 0; i < rangeCount; i++)
            {
                _multiDrawFirst[i] = _ranges[i].First;
                _multiDrawCount[i] = _ranges[i].Count;
            }

            GL.MultiDrawArrays(PrimitiveType.Points, _multiDrawFirst, _multiDrawCount, rangeCount);

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
            _store = null;

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

            _roots = Array.Empty<int>();
            _visits = Array.Empty<PointTileVisit>();
            _ranges = Array.Empty<PointDrawRange>();
            _multiDrawFirst = Array.Empty<int>();
            _multiDrawCount = Array.Empty<int>();
        }
    }
}
