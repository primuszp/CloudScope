using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Professional sphere gizmo renderer — visual style matches BoxGizmoRenderer.
    ///
    /// Layers:
    ///   1. Semi-transparent sphere fill   (neutral blue, 7% alpha)
    ///   2. Axis diameter lines            (X=red, Y=green, Z=blue, 65% alpha)
    ///   3. Three great-circle rings       (axis-colored, depth-tested + ghost)
    ///   4. Handle diamonds                (center=green, poles=white, hover=yellow)
    /// </summary>
    public sealed class SphereGizmoRenderer : IDisposable
    {
        // GL resources
        private int _fillVao = -1, _fillVbo = -1;    // UV-sphere fill triangles
        private int _circVao = -1, _circVbo = -1;    // 3 great-circle line loops
        private int _axisVao = -1, _axisVbo = -1;    // 3 diameter lines
        private int _dynVao  = -1, _dynVbo  = -1;    // dynamic: handle diamonds
        private int _shader  = -1;
        private int _uMVP, _uColor;

        private int _fillVertCount;
        private const int Seg   = 64;   // circle segments
        private const int Lat   = 16;   // UV sphere latitudes
        private const int Lon   = 32;   // UV sphere longitudes

        // Same axis colors as BoxGizmoRenderer
        private static readonly Vector4[] AxisColor =
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

        // ── Public entry points ───────────────────────────────────────────────

        public void Render(Vector3 center, float radius, Matrix4 view, Matrix4 proj,
                           EditAction action, OrbitCamera cam, int hoveredHandle)
        {
            if (radius < 1e-5f) return;
            EnsureResources();

            Matrix4 model = Matrix4.CreateScale(radius) * Matrix4.CreateTranslation(center);
            Matrix4 mvp   = model * view * proj;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            RenderFill(mvp);
            RenderAxes(mvp);
            RenderCircles(mvp);
            if (cam != null)
                RenderHandles(center, radius, cam, hoveredHandle);

            GL.Disable(EnableCap.Blend);
        }

        // Backwards-compatible overload (no handles)
        public void Render(Vector3 center, float radius, Matrix4 view, Matrix4 proj,
                           EditAction action = EditAction.None)
            => Render(center, radius, view, proj, action, null!, SphereSelectionTool.HoverNone);

        // ── Layer 1: Transparent sphere fill ─────────────────────────────────

        private void RenderFill(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.BindVertexArray(_fillVao);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);
            SetColor(0.30f, 0.60f, 0.95f, 0.07f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _fillVertCount);
            GL.DepthMask(true);
        }

        // ── Layer 2: Axis diameter lines ──────────────────────────────────────

        private void RenderAxes(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.BindVertexArray(_axisVao);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.LineWidth(1.5f);
            for (int ax = 0; ax < 3; ax++)
            {
                var c = AxisColor[ax];
                SetColor(c.X, c.Y, c.Z, 0.65f);
                GL.DrawArrays(PrimitiveType.Lines, ax * 2, 2);
            }
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 3: Great-circle rings (axis-colored, depth + ghost) ─────────

        private void RenderCircles(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.BindVertexArray(_circVao);
            GL.DepthMask(false);

            for (int ax = 0; ax < 3; ax++)
            {
                var c = AxisColor[ax];
                int offset = ax * Seg * 2;

                GL.Enable(EnableCap.DepthTest);
                SetColor(c.X, c.Y, c.Z, 0.80f);
                GL.LineWidth(2.0f);
                GL.DrawArrays(PrimitiveType.Lines, offset, Seg * 2);

                GL.Disable(EnableCap.DepthTest);
                SetColor(c.X, c.Y, c.Z, 0.18f);
                GL.LineWidth(1.0f);
                GL.DrawArrays(PrimitiveType.Lines, offset, Seg * 2);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 4: Handle diamonds (screen-space NDC) ───────────────────────

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
                Vector3 worldPos = SphereHandleWorld(center, radius, i);
                var (sx, sy, behind) = cam.WorldToScreen(worldPos);
                if (behind) continue;

                float nx = sx / vpW * 2f - 1f, ny = 1f - sy / vpH * 2f;
                float size = 12f;
                float hx = size / vpW, hy = size / vpH;

                Vector4 col = i == hoveredHandle
                    ? new(1f, 1f, 0.15f, 1f)
                    : i == 0
                        ? new(0.3f, 1f, 0.45f, 0.85f)
                        : new(0.9f, 0.9f, 0.9f, 0.80f);

                Dyn(new[] {
                    nx,    ny+hy, 0f,  nx+hx, ny,    0f,  nx,    ny-hy, 0f,
                    nx,    ny+hy, 0f,  nx,    ny-hy, 0f,  nx-hx, ny,    0f,
                });
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

                Dyn(new[] { nx, ny+hy, 0f, nx+hx, ny, 0f, nx, ny-hy, 0f, nx-hx, ny, 0f, nx, ny+hy, 0f });
                SetColor(col.X * 0.4f, col.Y * 0.4f, col.Z * 0.4f, 0.7f);
                GL.LineWidth(1f);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, 5);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        private static Vector3 SphereHandleWorld(Vector3 center, float radius, int i) => i switch
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

            // ── UV-sphere fill (Lat stacks × Lon slices) ─────────────────────
            _fillVertCount = Lat * Lon * 6;
            float[] fill = new float[_fillVertCount * 3];
            int fi = 0;
            for (int la = 0; la < Lat; la++)
            {
                float phi0 = MathF.PI * la       / Lat - MathF.PI * 0.5f;
                float phi1 = MathF.PI * (la + 1) / Lat - MathF.PI * 0.5f;
                float cp0 = MathF.Cos(phi0), sp0 = MathF.Sin(phi0);
                float cp1 = MathF.Cos(phi1), sp1 = MathF.Sin(phi1);
                for (int lo = 0; lo < Lon; lo++)
                {
                    float th0 = MathF.Tau * lo       / Lon;
                    float th1 = MathF.Tau * (lo + 1) / Lon;
                    float ct0 = MathF.Cos(th0), st0 = MathF.Sin(th0);
                    float ct1 = MathF.Cos(th1), st1 = MathF.Sin(th1);
                    // v00, v10, v11,  v00, v11, v01
                    void P(float cp, float sp, float ct, float st)
                    { fill[fi++] = cp * ct; fill[fi++] = sp; fill[fi++] = cp * st; }
                    P(cp0,sp0,ct0,st0); P(cp1,sp1,ct0,st0); P(cp1,sp1,ct1,st1);
                    P(cp0,sp0,ct0,st0); P(cp1,sp1,ct1,st1); P(cp0,sp0,ct1,st1);
                }
            }
            MakeStaticVao(ref _fillVao, ref _fillVbo, fill);

            // ── 3 great-circle rings (axis-colored) ───────────────────────────
            // Circle 0 (YZ, around X): red   → verts: (0, cos, sin)
            // Circle 1 (XZ, around Y): green → verts: (cos, 0, sin)
            // Circle 2 (XY, around Z): blue  → verts: (cos, sin, 0)
            float[] circ = new float[3 * Seg * 2 * 3];
            int ci = 0;
            float step = MathF.Tau / Seg;
            for (int s = 0; s < Seg; s++)
            {
                float a1 = s * step, a2 = (s + 1) * step;
                // X-axis ring (YZ plane)
                circ[ci++]=0f; circ[ci++]=MathF.Cos(a1); circ[ci++]=MathF.Sin(a1);
                circ[ci++]=0f; circ[ci++]=MathF.Cos(a2); circ[ci++]=MathF.Sin(a2);
            }
            for (int s = 0; s < Seg; s++)
            {
                float a1 = s * step, a2 = (s + 1) * step;
                // Y-axis ring (XZ plane)
                circ[ci++]=MathF.Cos(a1); circ[ci++]=0f; circ[ci++]=MathF.Sin(a1);
                circ[ci++]=MathF.Cos(a2); circ[ci++]=0f; circ[ci++]=MathF.Sin(a2);
            }
            for (int s = 0; s < Seg; s++)
            {
                float a1 = s * step, a2 = (s + 1) * step;
                // Z-axis ring (XY plane)
                circ[ci++]=MathF.Cos(a1); circ[ci++]=MathF.Sin(a1); circ[ci++]=0f;
                circ[ci++]=MathF.Cos(a2); circ[ci++]=MathF.Sin(a2); circ[ci++]=0f;
            }
            MakeStaticVao(ref _circVao, ref _circVbo, circ);

            // ── 3 axis diameter lines ─────────────────────────────────────────
            float[] axes = {
                -1f, 0f, 0f,   1f, 0f, 0f,   // X
                 0f,-1f, 0f,   0f, 1f, 0f,   // Y
                 0f, 0f,-1f,   0f, 0f, 1f,   // Z
            };
            MakeStaticVao(ref _axisVao, ref _axisVbo, axes);

            // ── Dynamic (handles) ─────────────────────────────────────────────
            _dynVao = GL.GenVertexArray(); _dynVbo = GL.GenBuffer();
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);
        }

        private static void MakeStaticVao(ref int vao, ref int vbo, float[] data)
        {
            vao = GL.GenVertexArray(); vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);
        }

        public void Dispose()
        {
            if (_shader  != -1) GL.DeleteProgram(_shader);
            if (_fillVao != -1) { GL.DeleteVertexArray(_fillVao); GL.DeleteBuffer(_fillVbo); }
            if (_circVao != -1) { GL.DeleteVertexArray(_circVao); GL.DeleteBuffer(_circVbo); }
            if (_axisVao != -1) { GL.DeleteVertexArray(_axisVao); GL.DeleteBuffer(_axisVbo); }
            if (_dynVao  != -1) { GL.DeleteVertexArray(_dynVao);  GL.DeleteBuffer(_dynVbo);  }
        }
    }
}
