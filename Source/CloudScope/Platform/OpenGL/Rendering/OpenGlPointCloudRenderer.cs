using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    public sealed class OpenGlPointCloudRenderer : IPointCloudRenderer
    {
        private const int PointStride = 24; // 6 floats
        private const int DefaultMaxResidentPoints = 5_000_000;
        private const int DefaultMaxDrawPointsPerFrame = 5_000_000;
        private static readonly int MaxDrawPointsPerFrame =
            int.TryParse(Environment.GetEnvironmentVariable("CLOUDSCOPE_OPENGL_MAX_DRAW_POINTS"), out int maxDrawPoints) && maxDrawPoints > 0
                ? maxDrawPoints
                : DefaultMaxDrawPointsPerFrame;
        private static readonly int MaxResidentPoints =
            int.TryParse(Environment.GetEnvironmentVariable("CLOUDSCOPE_OPENGL_MAX_RESIDENT_POINTS"), out int maxResidentPoints) && maxResidentPoints > 0
                ? maxResidentPoints
                : DefaultMaxResidentPoints;

        private int _vao = -1, _vbo = -1;
        private int _shader = -1;
        private int _uView, _uProj, _uPointSize;
        private int _pointCount;

        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aCol;

out vec3 vColor;

uniform mat4 view;
uniform mat4 projection;
uniform float pointSize;

void main()
{
    gl_Position  = projection * view * vec4(aPos, 1.0);
    gl_PointSize = pointSize;
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

        public void Initialize()
        {
            _shader = BuildShader(VertSrc, FragSrc);
            _uView = GL.GetUniformLocation(_shader, "view");
            _uProj = GL.GetUniformLocation(_shader, "projection");
            _uPointSize = GL.GetUniformLocation(_shader, "pointSize");
        }

        public void Upload(PointCloudRenderData data)
        {
            ReleasePointBuffers();
            PointData[] points = data.Points;
            int[]? renderOrder = data.RenderOrder;
            int requestedCount = data.Count;
            _pointCount = Math.Min(requestedCount, MaxResidentPoints);

            if (_pointCount == 0)
                return;

            PointData[] uploadPoints = points;
            if (renderOrder is { Length: > 0 })
                uploadPoints = BuildOrderedPointBuffer(points, renderOrder, _pointCount);

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _pointCount * PointStride, uploadPoints, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, PointStride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, PointStride, 12);
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(0);
        }

        public int Render(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius)
        {
            if (_pointCount <= 0)
                return 0;

            int drawCount = PointDrawBudget.Compute(
                _pointCount,
                halfViewSize,
                cloudRadius,
                Math.Min(MaxDrawPointsPerFrame, _pointCount));

            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProj, false, ref projection);
            GL.Uniform1(_uPointSize, pointSize);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Points, 0, drawCount);

            return drawCount;
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

            if (_vao != -1)
            {
                GL.DeleteVertexArray(_vao);
                _vao = -1;
            }
        }

        private static PointData[] BuildOrderedPointBuffer(PointData[] points, int[] renderOrder, int count)
        {
            var ordered = new PointData[count];
            for (int i = 0; i < count; i++)
                ordered[i] = points[renderOrder[i]];
            return ordered;
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
