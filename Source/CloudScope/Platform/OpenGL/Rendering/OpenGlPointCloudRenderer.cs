using System;
using System.Buffers;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Loading;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    internal sealed class OpenGlPointCloudRenderer : IPointCloudRenderer
    {
        private const int PointStride = GpuPointVertex.Stride;
        private const int AttributeStride = GpuPointAttribute.Stride;
        private static readonly PointRenderLimits Limits = PointRenderLimits.Load("OPENGL");

        private readonly OpenGlPointCloudProgram _program = new();
        private int _vao = -1, _vbo = -1, _attributeVbo = -1;
        private int _pointCount;
        private bool _hasAttributes;
        private bool _hasSourceColors;
        private ColorSource _colorSource = ColorSource.Rgb;
        private PointOctreeNode[] _nodes = Array.Empty<PointOctreeNode>();
        private PointDrawRange[] _drawRanges = Array.Empty<PointDrawRange>();
        private PointLodScratch _lodScratch = new(0);
        private int[] _multiDrawFirst = Array.Empty<int>();
        private int[] _multiDrawCount = Array.Empty<int>();

        public int PointCount => _pointCount;
        public bool SupportsAttributeColoring => true;
        public bool CanUpdateColorSourceWithoutUpload => _hasAttributes && _hasSourceColors;

        public void Initialize() => _program.Initialize();

        public void Upload(PointCloudRenderData data)
        {
            ReleasePointBuffers();
            int requestedCount = data.Count;
            // OpenGL has no portable way to ask how much memory the device has, so the cloud
            // is bounded only by CLOUDSCOPE_MAX_RESIDENT_POINTS here.
            _pointCount = Math.Min(requestedCount, Limits.ResolveResidentLimit(null, 0));
            _hasAttributes = data.HasAttributes;
            _hasSourceColors = data.HasSourceColors;
            _colorSource = data.ColorSource;

            if (_pointCount == 0)
                return;

            using PointCloudOctreeLayout layout = PointCloudOctree.Build(data, _pointCount);
            _nodes = layout.Nodes;
            _drawRanges = new PointDrawRange[_nodes.Length];
            _lodScratch = new PointLodScratch(_nodes.Length);
            _multiDrawFirst = new int[_nodes.Length];
            _multiDrawCount = new int[_nodes.Length];

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GpuPointVertex[] uploadPoints = ArrayPool<GpuPointVertex>.Shared.Rent(_pointCount);
            try
            {
                PointRenderUploadBuilder.FillPoints(data, uploadPoints.AsSpan(0, _pointCount), uploadOrder: layout.UploadOrder);
                GL.BufferData(BufferTarget.ArrayBuffer, _pointCount * PointStride, uploadPoints, BufferUsageHint.StaticDraw);
            }
            finally
            {
                ArrayPool<GpuPointVertex>.Shared.Return(uploadPoints);
            }

            OpenGlPointCloudProgram.BindPositionAttributes();

            if (data.HasAttributes && _pointCount > 0)
            {
                _attributeVbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, _attributeVbo);
                GpuPointAttribute[] uploadAttributes = ArrayPool<GpuPointAttribute>.Shared.Rent(_pointCount);
                try
                {
                    PointRenderAttributeBuilder.Fill(
                        data, uploadAttributes.AsSpan(0, _pointCount), uploadOrder: layout.UploadOrder);
                    GL.BufferData(
                        BufferTarget.ArrayBuffer,
                        _pointCount * AttributeStride,
                        uploadAttributes,
                        BufferUsageHint.StaticDraw);
                }
                finally
                {
                    ArrayPool<GpuPointAttribute>.Shared.Return(uploadAttributes);
                }

                OpenGlPointCloudProgram.BindAttributeAttributes();
            }

            GL.BindVertexArray(0);
        }

        public void UpdateColorSource(ColorSource source) => _colorSource = source;

        public int Render(IRenderFrameData frameData, in PointRenderView renderView)
        {
            if (_pointCount <= 0)
                return 0;

            int drawBudget = Limits.GetFrameBudget(_pointCount);

            int rangeCount = PointLodPlanner.Plan(
                _nodes, in renderView, drawBudget, _drawRanges, _lodScratch, out int drawnPointCount);
            if (rangeCount == 0)
                return 0;

            _program.Use(in renderView, _colorSource, _hasAttributes);
            GL.BindVertexArray(_vao);

            // One MultiDrawArrays instead of a call per cell: a large cloud plans a couple of
            // thousand ranges every frame, and that many driver round trips shows up in the
            // frame time on its own.
            for (int i = 0; i < rangeCount; i++)
            {
                _multiDrawFirst[i] = _drawRanges[i].First;
                _multiDrawCount[i] = _drawRanges[i].Count;
            }

            GL.MultiDrawArrays(PrimitiveType.Points, _multiDrawFirst, _multiDrawCount, rangeCount);

            return drawnPointCount;
        }

        public void Dispose()
        {
            ReleasePointBuffers();
            _program.Dispose();
        }

        private void ReleasePointBuffers()
        {
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
            _nodes = Array.Empty<PointOctreeNode>();
            _drawRanges = Array.Empty<PointDrawRange>();
            _lodScratch = new PointLodScratch(0);
            _multiDrawFirst = Array.Empty<int>();
            _multiDrawCount = Array.Empty<int>();
        }

    }
}
