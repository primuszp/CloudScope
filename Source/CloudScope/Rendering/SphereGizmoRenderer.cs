using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    public sealed class SphereGizmoRenderer : IDisposable
    {
        private int _vao = -1, _vbo = -1;        // static: sphere wireframe circles
        private int _dynVao = -1, _dynVbo = -1;  // dynamic: handle diamonds
        private int _shader = -1;
        private int _uMVP, _uColor;
        private const int Segments = 64;
        private int _vertexCount;

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

        public void Render(Vector3 center, float radius, Matrix4 view, Matrix4 proj,
                           EditAction action, OrbitCamera cam, int hoveredHandle)
        {
            if (radius < 1e-5f) return;
            EnsureResources();

            RenderWireframe(center, radius, view, proj, action);

            if (cam != null)
                RenderHandles(center, radius, cam, hoveredHandle);
        }

        // Backwards-compatible overload (no handles)
        public void Render(Vector3 center, float radius, Matrix4 view, Matrix4 proj,
                           EditAction action = EditAction.None)
            => Render(center, radius, view, proj, action, null!, SphereSelectionTool.HoverNone);

        // ── Sphere wireframe circles ──────────────────────────────────────────

        private void RenderWireframe(Vector3 center, float radius, Matrix4 view, Matrix4 proj,
                                     EditAction action)
        {
            Matrix4 model = Matrix4.CreateScale(radius) * Matrix4.CreateTranslation(center);
            Matrix4 mvp = model * view * proj;

            Vector4 color = action switch
            {
                EditAction.Grab  => new Vector4(0.2f, 1.0f, 0.3f, 0.7f),
                EditAction.Scale => new Vector4(1.0f, 0.6f, 0.1f, 0.7f),
                _                => new Vector4(1.0f, 0.6f, 0.15f, 0.7f),
            };

            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.BindVertexArray(_vao);

            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Uniform4(_uColor, color.X, color.Y, color.Z, color.W);
            GL.LineWidth(1.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);

            GL.Disable(EnableCap.DepthTest);
            GL.Uniform4(_uColor, color.X, color.Y, color.Z, color.W * 0.25f);
            GL.LineWidth(1.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Handle diamonds (screen-space NDC, same as BoxGizmoRenderer) ──────

        private void RenderHandles(Vector3 center, float radius, OrbitCamera cam, int hoveredHandle)
        {
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            Matrix4 id = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            for (int i = 0; i < SphereSelectionTool.HandleCount; i++)
            {
                Vector3 worldPos = i == 0 ? center : ComputePoleWorld(center, radius, i);
                var (sx, sy, behind) = cam.WorldToScreen(worldPos);
                if (behind) continue;

                float nx = sx / vpW * 2f - 1f, ny = 1f - sy / vpH * 2f;
                float size = i == 0 ? 7f : 9f;
                float hx = size / vpW, hy = size / vpH;

                Vector4 col = i == hoveredHandle
                    ? new(1f, 1f, 0.15f, 1f)
                    : i == 0
                        ? new(0.3f, 1f, 0.45f, 0.85f)   // center: green
                        : new(0.9f, 0.9f, 0.9f, 0.80f); // pole: white

                // Diamond fill (4 triangles)
                Dyn(new[] {
                    nx,    ny+hy, 0f,  nx+hx, ny,    0f,  nx,    ny-hy, 0f,
                    nx,    ny+hy, 0f,  nx,    ny-hy, 0f,  nx-hx, ny,    0f,
                });
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

                // Diamond outline
                Dyn(new[] { nx, ny+hy, 0f, nx+hx, ny, 0f, nx, ny-hy, 0f, nx-hx, ny, 0f, nx, ny+hy, 0f });
                SetColor(col.X * 0.4f, col.Y * 0.4f, col.Z * 0.4f, 0.7f);
                GL.LineWidth(1f);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, 5);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        private static Vector3 ComputePoleWorld(Vector3 center, float radius, int i) => i switch
        {
            1 => center + new Vector3( radius, 0f, 0f),
            2 => center + new Vector3(-radius, 0f, 0f),
            3 => center + new Vector3(0f,  radius, 0f),
            4 => center + new Vector3(0f, -radius, 0f),
            5 => center + new Vector3(0f, 0f,  radius),
            6 => center + new Vector3(0f, 0f, -radius),
            _ => center
        };

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Dyn(float[] data)
        {
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.DynamicDraw);
        }

        private void SetColor(float r, float g, float b, float a) => GL.Uniform4(_uColor, r, g, b, a);
        private void SetColor(Vector4 c) => GL.Uniform4(_uColor, c.X, c.Y, c.Z, c.W);

        // ── Resource init ─────────────────────────────────────────────────────

        private void EnsureResources()
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

            // Static: sphere wireframe circles
            _vertexCount = 3 * Segments * 2;
            float[] verts = new float[_vertexCount * 3];
            int idx = 0;
            float step = MathF.PI * 2f / Segments;
            for (int i = 0; i < Segments; i++)
            {
                float a1 = i * step, a2 = (i + 1) * step;
                verts[idx++] = MathF.Cos(a1); verts[idx++] = MathF.Sin(a1); verts[idx++] = 0f;
                verts[idx++] = MathF.Cos(a2); verts[idx++] = MathF.Sin(a2); verts[idx++] = 0f;
            }
            for (int i = 0; i < Segments; i++)
            {
                float a1 = i * step, a2 = (i + 1) * step;
                verts[idx++] = MathF.Cos(a1); verts[idx++] = 0f; verts[idx++] = MathF.Sin(a1);
                verts[idx++] = MathF.Cos(a2); verts[idx++] = 0f; verts[idx++] = MathF.Sin(a2);
            }
            for (int i = 0; i < Segments; i++)
            {
                float a1 = i * step, a2 = (i + 1) * step;
                verts[idx++] = 0f; verts[idx++] = MathF.Cos(a1); verts[idx++] = MathF.Sin(a1);
                verts[idx++] = 0f; verts[idx++] = MathF.Cos(a2); verts[idx++] = MathF.Sin(a2);
            }
            _vao = GL.GenVertexArray(); _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);

            // Dynamic: handle diamonds
            _dynVao = GL.GenVertexArray(); _dynVbo = GL.GenBuffer();
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);
        }

        public void Dispose()
        {
            if (_shader  != -1) GL.DeleteProgram(_shader);
            if (_vao     != -1) { GL.DeleteVertexArray(_vao);    GL.DeleteBuffer(_vbo);    }
            if (_dynVao  != -1) { GL.DeleteVertexArray(_dynVao); GL.DeleteBuffer(_dynVbo); }
        }
    }
}
