using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Shared OpenGL infrastructure for all gizmo renderers.
    /// Concrete renderers inherit this and call EnsureBaseResources() on first render.
    /// </summary>
    public abstract class GizmoRendererBase : IDisposable
    {
        protected int _shader = -1, _uMVP, _uColor;
        protected int _dynVao = -1, _dynVbo = -1;

        protected static readonly Vector4[] AxisColor =
        {
            new(0.95f, 0.20f, 0.20f, 1.00f),  // X  red
            new(0.20f, 0.95f, 0.30f, 1.00f),  // Y  green
            new(0.25f, 0.55f, 1.00f, 1.00f),  // Z  blue
        };

        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPos;
uniform mat4 uMVP;
void main() { gl_Position = uMVP * vec4(aPos, 1.0); }
";
        private const string FragSrc = @"
#version 330 core
uniform vec4 uColor;
out vec4 FragColor;
void main() { FragColor = uColor; }
";

        /// <summary>Compile shader and create dynamic VAO. Call at the top of EnsureResources().</summary>
        protected void EnsureBaseResources()
        {
            if (_shader != -1) return;

            int v = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(v, VertSrc); GL.CompileShader(v);
            int f = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(f, FragSrc); GL.CompileShader(f);
            _shader = GL.CreateProgram();
            GL.AttachShader(_shader, v); GL.AttachShader(_shader, f);
            GL.LinkProgram(_shader);
            GL.DeleteShader(v); GL.DeleteShader(f);
            _uMVP   = GL.GetUniformLocation(_shader, "uMVP");
            _uColor = GL.GetUniformLocation(_shader, "uColor");

            _dynVao = GL.GenVertexArray();
            _dynVbo = GL.GenBuffer();
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);
        }

        protected static void MakeStaticVao(ref int vao, ref int vbo, float[] data)
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);
        }

        protected void Dyn(float[] data, int count = -1)
        {
            int bytes = (count < 0 ? data.Length : count) * sizeof(float);
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, bytes, data, BufferUsageHint.DynamicDraw);
        }

        protected void SetColor(float r, float g, float b, float a) => GL.Uniform4(_uColor, r, g, b, a);
        protected void SetColor(Vector4 c) => GL.Uniform4(_uColor, c.X, c.Y, c.Z, c.W);

        /// <summary>Draw a screen-space diamond handle (fill + outline).</summary>
        protected void DrawDiamond(float nx, float ny, float hx, float hy, Vector4 col)
        {
            Dyn(new[]{
                nx,    ny+hy, 0f,  nx+hx, ny,    0f,  nx,    ny-hy, 0f,
                nx,    ny+hy, 0f,  nx,    ny-hy, 0f,  nx-hx, ny,    0f,
            });
            SetColor(col);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            Dyn(new[]{ nx, ny+hy, 0f, nx+hx, ny, 0f, nx, ny-hy, 0f, nx-hx, ny, 0f, nx, ny+hy, 0f });
            SetColor(col.X * 0.4f, col.Y * 0.4f, col.Z * 0.4f, 0.7f);
            GL.LineWidth(1f);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, 5);
        }

        public abstract void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam);

        public virtual void Dispose()
        {
            if (_shader != -1) { GL.DeleteProgram(_shader); _shader = -1; }
            if (_dynVao != -1) { GL.DeleteVertexArray(_dynVao); GL.DeleteBuffer(_dynVbo); _dynVao = -1; }
        }
    }
}
