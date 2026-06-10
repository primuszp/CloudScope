using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Shared OpenGL infrastructure for all gizmo renderers:
    /// shader, dynamic VAO, shared axis-line geometry, and screen-space draw helpers.
    /// Concrete renderers call EnsureBaseResources() on first use.
    /// </summary>
    public abstract class OpenGlGizmoRendererBase : ISelectionGizmoRenderer
    {
        protected int _shader = -1, _uMVP, _uColor;
        protected int _dynVao = -1, _dynVbo = -1;
        private   int _axisVao = -1, _axisVbo = -1;

        // Pre-allocated scratch buffers — avoids per-frame heap allocations.
        private readonly float[] _diamondFillBuf    = new float[18]; // 6 verts
        private readonly float[] _diamondOutlineBuf = new float[15]; // 5 verts
        private readonly float[] _lineBuf           = new float[6];  // 2-vert line
        private readonly float[] _arrowBuf          = new float[9];  // 3-vert arrowhead
        protected        float[] _ringSegBuf        = new float[64 * 6]; // ring segments (N=64)

        protected static readonly Vector4[] AxisColor =
        {
            new(0.95f, 0.20f, 0.20f, 1.00f),  // X  red
            new(0.20f, 0.95f, 0.30f, 1.00f),  // Y  green
            new(0.25f, 0.55f, 1.00f, 1.00f),  // Z  blue
        };

        private static readonly float[] AxisLineData =
        {
            -1f, 0f, 0f,   1f, 0f, 0f,
             0f,-1f, 0f,   0f, 1f, 0f,
             0f, 0f,-1f,   0f, 0f, 1f,
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

        // ── Init ──────────────────────────────────────────────────────────────

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

            MakeStaticVao(ref _axisVao, ref _axisVbo, AxisLineData);
        }

        // ── Static VAO helper ─────────────────────────────────────────────────

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

        // ── Dynamic draw ──────────────────────────────────────────────────────

        protected void Dyn(float[] data, int count = -1)
        {
            int bytes = (count < 0 ? data.Length : count) * sizeof(float);
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, bytes, data, BufferUsageHint.DynamicDraw);
        }

        protected void SetColor(float r, float g, float b, float a) => GL.Uniform4(_uColor, r, g, b, a);
        protected void SetColor(Vector4 c) => GL.Uniform4(_uColor, c.X, c.Y, c.Z, c.W);

        // ── NDC conversion ────────────────────────────────────────────────────

        protected static (float nx, float ny) ScreenToNdc(float sx, float sy, float vpW, float vpH)
            => (sx / vpW * 2f - 1f, 1f - sy / vpH * 2f);

        // ── Screen-space render state helpers ─────────────────────────────────

        /// <summary>Switch to screen-space (NDC) rendering: identity MVP, depth disabled.</summary>
        protected void BeginScreenSpaceRender()
        {
            Matrix4 id = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
        }

        /// <summary>Restore depth state after screen-space rendering.</summary>
        protected static void EndScreenSpaceRender()
        {
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Screen-space draw helpers ─────────────────────────────────────────

        protected void DrawLine(float x0, float y0, float x1, float y1)
        {
            _lineBuf[0] = x0; _lineBuf[1] = y0; _lineBuf[2] = 0f;
            _lineBuf[3] = x1; _lineBuf[4] = y1; _lineBuf[5] = 0f;
            Dyn(_lineBuf);
        }

        protected void DrawArrowHead(float tnx, float tny, float fnx, float fny, float hs, Vector4 col)
        {
            float dx = tnx - fnx, dy = tny - fny;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float nx = dx/len, ny = dy/len, px = -ny, py = nx;
            _arrowBuf[0] = tnx;                   _arrowBuf[1] = tny;                   _arrowBuf[2] = 0f;
            _arrowBuf[3] = tnx-nx*hs*2f+px*hs;   _arrowBuf[4] = tny-ny*hs*2f+py*hs;   _arrowBuf[5] = 0f;
            _arrowBuf[6] = tnx-nx*hs*2f-px*hs;   _arrowBuf[7] = tny-ny*hs*2f-py*hs;   _arrowBuf[8] = 0f;
            Dyn(_arrowBuf);
            SetColor(col);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        protected void DrawProfessionalArrow(
            float startX,
            float startY,
            float tipX,
            float tipY,
            float viewportWidth,
            float viewportHeight,
            Vector4 color,
            float lineWidth)
        {
            var (snx, sny) = ScreenToNdc(startX, startY, viewportWidth, viewportHeight);
            var (tnx, tny) = ScreenToNdc(tipX, tipY, viewportWidth, viewportHeight);

            DrawLine(snx, sny, tnx, tny);
            SetColor(color with { W = 0.35f });
            GL.LineWidth(lineWidth + 3f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);

            DrawLine(snx, sny, tnx, tny);
            SetColor(color with { W = 1f });
            GL.LineWidth(lineWidth);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);

            DrawDiamondFill(snx, sny, 5f / viewportWidth, 5f / viewportHeight, color with { W = 1f });
            DrawArrowHead(tnx, tny, snx, sny, 0.022f, color with { W = 1f });
        }

        // ── Screen-space diamond handles ──────────────────────────────────────

        /// <summary>Draw diamond fill only (no outline), using pre-allocated buffer.</summary>
        protected void DrawDiamondFill(float nx, float ny, float hx, float hy, Vector4 col)
        {
            _diamondFillBuf[0] = nx;    _diamondFillBuf[1]  = ny+hy; _diamondFillBuf[2]  = 0f;
            _diamondFillBuf[3] = nx+hx; _diamondFillBuf[4]  = ny;    _diamondFillBuf[5]  = 0f;
            _diamondFillBuf[6] = nx;    _diamondFillBuf[7]  = ny-hy; _diamondFillBuf[8]  = 0f;
            _diamondFillBuf[9] = nx;    _diamondFillBuf[10] = ny+hy; _diamondFillBuf[11] = 0f;
            _diamondFillBuf[12]= nx;    _diamondFillBuf[13] = ny-hy; _diamondFillBuf[14] = 0f;
            _diamondFillBuf[15]= nx-hx; _diamondFillBuf[16] = ny;    _diamondFillBuf[17] = 0f;
            Dyn(_diamondFillBuf);
            SetColor(col);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        /// <summary>Draw a screen-space diamond handle (fill + dark outline).</summary>
        protected void DrawDiamond(float nx, float ny, float hx, float hy, Vector4 col)
        {
            DrawDiamondFill(nx, ny, hx, hy, col);
            _diamondOutlineBuf[0]  = nx;    _diamondOutlineBuf[1]  = ny+hy; _diamondOutlineBuf[2]  = 0f;
            _diamondOutlineBuf[3]  = nx+hx; _diamondOutlineBuf[4]  = ny;    _diamondOutlineBuf[5]  = 0f;
            _diamondOutlineBuf[6]  = nx;    _diamondOutlineBuf[7]  = ny-hy; _diamondOutlineBuf[8]  = 0f;
            _diamondOutlineBuf[9]  = nx-hx; _diamondOutlineBuf[10] = ny;    _diamondOutlineBuf[11] = 0f;
            _diamondOutlineBuf[12] = nx;    _diamondOutlineBuf[13] = ny+hy; _diamondOutlineBuf[14] = 0f;
            Dyn(_diamondOutlineBuf);
            SetColor(col.X * 0.4f, col.Y * 0.4f, col.Z * 0.4f, 0.7f);
            GL.LineWidth(1f);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, 5);
        }

        // ── Shared axis-line rendering ────────────────────────────────────────

        /// <summary>Draw 3 local-axis diameter lines (X/Y/Z), depth-disabled, ghosted.</summary>
        protected void RenderAxisLines(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.BindVertexArray(_axisVao);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.LineWidth(1.5f);
            for (int ax = 0; ax < 3; ax++)
            {
                SetColor(AxisColor[ax].X, AxisColor[ax].Y, AxisColor[ax].Z, 0.65f);
                GL.DrawArrays(PrimitiveType.Lines, ax * 2, 2);
            }
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Abstract ──────────────────────────────────────────────────────────

        public abstract void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam);

        public virtual void Dispose()
        {
            if (_shader  != -1) { GL.DeleteProgram(_shader);                                _shader  = -1; }
            if (_dynVao  != -1) { GL.DeleteVertexArray(_dynVao);  GL.DeleteBuffer(_dynVbo); _dynVao  = -1; }
            if (_axisVao != -1) { GL.DeleteVertexArray(_axisVao); GL.DeleteBuffer(_axisVbo); _axisVao = -1; }
        }
    }
}
