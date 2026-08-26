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

        private int _vao = -1, _vbo = -1, _attributeVbo = -1;
        private int _shader = -1;
        private int _uView, _uProj, _uPointSize, _uColorSource, _uHasAttributes;
        private int _pointCount;
        private bool _hasAttributes;
        private bool _hasSourceColors;
        private ColorSource _colorSource = ColorSource.Rgb;
        private PointOctreeNode[] _nodes = Array.Empty<PointOctreeNode>();
        private PointDrawRange[] _drawRanges = Array.Empty<PointDrawRange>();
        private PointLodScratch _lodScratch = new(0);
        private int[] _multiDrawFirst = Array.Empty<int>();
        private int[] _multiDrawCount = Array.Empty<int>();

        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aCol;
layout(location = 2) in float aZ;
layout(location = 3) in float aIntensity;
layout(location = 4) in float aClass;
layout(location = 5) in float aReturn;
layout(location = 6) in vec3 aRgb;

out vec3 vColor;

uniform mat4 view;
uniform mat4 projection;
uniform float pointSize;
uniform int colorSource;
uniform bool hasAttributes;
uniform vec3 classPalette[256];

vec3 gradientColor(float t)
{
    t = clamp(t, 0.0, 1.0);
    return vec3(t, min(1.0, 2.0 * min(t, 1.0 - t)), 1.0 - t);
}

vec3 heightColor(float z)
{
    z = clamp(z, 0.0, 1.0);
    return vec3(z, 1.0 - abs(2.0 * z - 1.0), 1.0 - z);
}

void main()
{
    gl_Position  = projection * view * vec4(aPos, 1.0);
    gl_PointSize = pointSize;
    if (!hasAttributes)
        vColor = aCol.rgb;
    else if (colorSource == 0)
        vColor = aRgb;
    else if (colorSource == 1)
        vColor = heightColor(aZ);
    else if (colorSource == 2)
        vColor = classPalette[int(clamp(aClass, 0.0, 255.0))];
    else if (colorSource == 3)
        vColor = gradientColor(aIntensity);
    else if (colorSource == 4)
        vColor = classPalette[int(clamp(aReturn, 0.0, 255.0))];
    else
        vColor = aCol.rgb;
}
";

        private const string FragSrc = @"
#version 330 core
in  vec3 vColor;
out vec4 FragColor;

void main()
{
    // Square points - no discard, preserves early-z and avoids
    // per-fragment branch divergence. Visually indistinguishable
    // at typical point cloud densities.
    FragColor = vec4(vColor, 1.0);
}
";

        public int PointCount => _pointCount;
        public bool SupportsAttributeColoring => true;
        public bool CanUpdateColorSourceWithoutUpload => _hasAttributes && _hasSourceColors;

        public void Initialize()
        {
            _shader = OpenGlShaderCompiler.CreateProgram(VertSrc, FragSrc, "point cloud");
            _uView = GL.GetUniformLocation(_shader, "view");
            _uProj = GL.GetUniformLocation(_shader, "projection");
            _uPointSize = GL.GetUniformLocation(_shader, "pointSize");
            _uColorSource = GL.GetUniformLocation(_shader, "colorSource");
            _uHasAttributes = GL.GetUniformLocation(_shader, "hasAttributes");
            UploadClassPalette();
        }

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

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, PointStride, 0);
            GL.EnableVertexAttribArray(0);
            // Normalized unsigned bytes: the shader still sees a 0..1 color.
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, PointStride, 12);
            GL.EnableVertexAttribArray(1);

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

                // Height and intensity are normalized 16-bit; class and return number are raw
                // byte codes, so they are not normalized; the source color is normalized bytes.
                GL.VertexAttribPointer(2, 1, VertexAttribPointerType.UnsignedShort, true, AttributeStride, 0);
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(3, 1, VertexAttribPointerType.UnsignedShort, true, AttributeStride, 2);
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribPointer(4, 1, VertexAttribPointerType.UnsignedByte, false, AttributeStride, 8);
                GL.EnableVertexAttribArray(4);
                GL.VertexAttribPointer(5, 1, VertexAttribPointerType.UnsignedByte, false, AttributeStride, 9);
                GL.EnableVertexAttribArray(5);
                GL.VertexAttribPointer(6, 3, VertexAttribPointerType.UnsignedByte, true, AttributeStride, 4);
                GL.EnableVertexAttribArray(6);
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

            Matrix4 view = renderView.View;
            Matrix4 projection = renderView.Projection;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProj, false, ref projection);
            GL.Uniform1(_uPointSize, renderView.PointSize);
            GL.Uniform1(_uColorSource, PointRenderAttributeBuilder.MapColorSource(_colorSource));
            GL.Uniform1(_uHasAttributes, _hasAttributes ? 1 : 0);
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
            if (_shader != -1) GL.DeleteProgram(_shader);
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

        private void UploadClassPalette()
        {
            GL.UseProgram(_shader);
            for (int i = 0; i < 256; i++)
            {
                int location = GL.GetUniformLocation(_shader, $"classPalette[{i}]");
                if (location < 0)
                    continue;

                var color = ClassColorPalette.GetColor((byte)i);
                GL.Uniform3(location, color);
            }
        }

    }
}
