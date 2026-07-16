using System;
using System.Buffers;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Loading;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    public sealed class OpenGlPointCloudRenderer : IPointCloudRenderer
    {
        private const int PointStride = 24; // 6 floats
        private const int AttributeStride = 28; // 7 floats
        private static readonly PointRenderLimits Limits = PointRenderLimits.Load("OPENGL");

        private int _vao = -1, _vbo = -1, _attributeVbo = -1;
        private int _shader = -1;
        private int _uView, _uProj, _uPointSize, _uColorSource, _uHasAttributes;
        private int _pointCount;
        private bool _hasAttributes;
        private bool _hasSourceColors;
        private ColorSource _colorSource = ColorSource.Rgb;
        private PointRenderChunk[] _chunks = Array.Empty<PointRenderChunk>();
        private PointDrawRange[] _drawRanges = Array.Empty<PointDrawRange>();

        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aCol;
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
        vColor = aCol;
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
        vColor = aCol;
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
            _shader = BuildShader(VertSrc, FragSrc);
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
            _pointCount = Math.Min(requestedCount, Limits.MaxResidentPoints);
            _hasAttributes = data.HasAttributes;
            _hasSourceColors = data.HasSourceColors;
            _colorSource = data.ColorSource;

            if (_pointCount == 0)
                return;

            using PointSpatialUploadLayout layout = PointRenderUploadBuilder.BuildSpatialLayout(data, _pointCount);
            _chunks = layout.Chunks;
            _drawRanges = new PointDrawRange[_chunks.Length];

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            PointData[] uploadPoints = ArrayPool<PointData>.Shared.Rent(_pointCount);
            try
            {
                PointRenderUploadBuilder.FillPoints(data, uploadPoints.AsSpan(0, _pointCount), uploadOrder: layout.UploadOrder);
                GL.BufferData(BufferTarget.ArrayBuffer, _pointCount * PointStride, uploadPoints, BufferUsageHint.StaticDraw);
            }
            finally
            {
                ArrayPool<PointData>.Shared.Return(uploadPoints);
            }

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, PointStride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, PointStride, 12);
            GL.EnableVertexAttribArray(1);

            if (data.HasAttributes && _pointCount > 0)
            {
                _attributeVbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, _attributeVbo);
                PointRenderAttributeData[] uploadAttributes = ArrayPool<PointRenderAttributeData>.Shared.Rent(_pointCount);
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
                    ArrayPool<PointRenderAttributeData>.Shared.Return(uploadAttributes);
                }

                GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, AttributeStride, 0);
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, AttributeStride, 4);
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, AttributeStride, 8);
                GL.EnableVertexAttribArray(4);
                GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, AttributeStride, 12);
                GL.EnableVertexAttribArray(5);
                GL.VertexAttribPointer(6, 3, VertexAttribPointerType.Float, false, AttributeStride, 16);
                GL.EnableVertexAttribArray(6);
            }

            GL.BindVertexArray(0);
        }

        public void UpdateColorSource(ColorSource source) => _colorSource = source;

        public int Render(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius)
        {
            if (_pointCount <= 0)
                return 0;

            int drawCount = PointDrawBudget.Compute(
                _pointCount,
                halfViewSize,
                cloudRadius,
                Math.Min(Limits.MaxDrawPointsPerFrame, _pointCount));

            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProj, false, ref projection);
            GL.Uniform1(_uPointSize, pointSize);
            GL.Uniform1(_uColorSource, PointRenderAttributeBuilder.MapColorSource(_colorSource));
            GL.Uniform1(_uHasAttributes, _hasAttributes ? 1 : 0);
            GL.BindVertexArray(_vao);
            int rangeCount = PointChunkDrawPlanner.FillDrawRanges(
                _chunks, ref view, ref projection, drawCount, _drawRanges, out int drawnPointCount);
            for (int i = 0; i < rangeCount; i++)
            {
                PointDrawRange range = _drawRanges[i];
                GL.DrawArrays(PrimitiveType.Points, range.First, range.Count);
            }

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
            _chunks = Array.Empty<PointRenderChunk>();
            _drawRanges = Array.Empty<PointDrawRange>();
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

        private static int BuildShader(string vertSrc, string fragSrc)
        {
            int v = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(v, vertSrc);
            GL.CompileShader(v);
            CheckShader(v, "vertex");

            int f = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(f, fragSrc);
            GL.CompileShader(f);
            CheckShader(f, "fragment");

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, v);
            GL.AttachShader(prog, f);
            GL.LinkProgram(prog);

            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
                throw new InvalidOperationException("Shader link:\n" + GL.GetProgramInfoLog(prog));

            GL.DeleteShader(v);
            GL.DeleteShader(f);
            return prog;
        }

        private static void CheckShader(int shader, string name)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
                throw new InvalidOperationException($"{name} shader:\n" + GL.GetShaderInfoLog(shader));
        }
    }
}
