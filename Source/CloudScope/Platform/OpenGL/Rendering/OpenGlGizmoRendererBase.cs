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
    internal abstract class OpenGlGizmoRendererBase : ISelectionGizmoRenderer
    {
        protected const int SmoothRingSegments = 128;
        protected int _shader = -1, _uMVP, _uColor;
        private readonly OpenGlWideLineRenderer _wideLines = new();
        private readonly OpenGlJoinedLineRenderer _joinedLines = new();
        private Matrix4 _currentMvp = Matrix4.Identity;
        protected int _dynVao = -1, _dynVbo = -1;
        private   int _axisVao = -1, _axisVbo = -1;

        // Pre-allocated scratch buffers — avoids per-frame heap allocations.
        private readonly float[] _diamondFillBuf    = new float[18]; // 6 verts
        private readonly float[] _diamondOutlineBuf = new float[15]; // 5 verts
        private readonly float[] _lineBuf           = new float[6];  // 2-vert line
        private readonly float[] _arrowBuf          = new float[9];  // 3-vert arrowhead
        protected        float[] _ringSegBuf        = new float[SmoothRingSegments * 3];

        // 3-D cone arrowhead buffers
        private const int ConeSeg = 12;
        private readonly float[]   _coneBuf  = new float[ConeSeg * 9]; // sides: N triangles * 3 verts * 3 floats
        private readonly float[]   _capBuf   = new float[ConeSeg * 9]; // cap disc
        private readonly Vector3[] _coneRing = new Vector3[ConeSeg + 1];

        protected static readonly Vector4[] AxisColor =
        {
            AxisPalette.Of(0, 1.00f),
            AxisPalette.Of(1, 1.00f),
            AxisPalette.Of(2, 1.00f),
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

            _shader = OpenGlShaderCompiler.CreateProgram(VertSrc, FragSrc, "gizmo");
            _uMVP   = GL.GetUniformLocation(_shader, "uMVP");
            _uColor = GL.GetUniformLocation(_shader, "uColor");

            _dynVao = GL.GenVertexArray();
            _dynVbo = GL.GenBuffer();
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);

            MakeStaticVao(ref _axisVao, ref _axisVbo, AxisLineData);
            _wideLines.EnsureResources();
            _joinedLines.EnsureResources();
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

        // ── Lines ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the transform for the following draws. It is remembered so that
        /// <see cref="DrawLines"/> can hand the same matrix to the wide-line shader.
        /// </summary>
        protected void SetMvp(ref Matrix4 mvp)
        {
            _currentMvp = mvp;
            GL.UniformMatrix4(_uMVP, false, ref mvp);
        }

        /// <summary>
        /// Draws a line list from <paramref name="vbo"/> at a real pixel width. Widths above
        /// one pixel go through the quad-expanding shader, because core-profile OpenGL will
        /// not widen native lines (see <see cref="LineWidth"/>).
        /// </summary>
        protected void DrawLines(int vbo, int firstVertex, int vertexCount, Vector4 color, float widthPixels, int vertexStride = 3 * sizeof(float))
        {
            if (LineWidth.NeedsExpansion(widthPixels))
            {
                _wideLines.Draw(vbo, firstVertex, vertexCount, ref _currentMvp, color, widthPixels, vertexStride);
                return;
            }

            SetColor(color);
            GL.LineWidth(widthPixels);
            GL.DrawArrays(PrimitiveType.Lines, firstVertex, vertexCount);
        }

        /// <summary>Draws a line list previously uploaded with <see cref="Dyn"/>.</summary>
        protected void DrawDynamicLines(int firstVertex, int vertexCount, Vector4 color, float widthPixels)
            => DrawLines(_dynVbo, firstVertex, vertexCount, color, widthPixels);

        /// <summary>
        /// Draws unique points as one closed, joined ribbon. Unlike a line list this creates
        /// no independently rasterized segment ends, matching the smooth pivot-ring path.
        /// </summary>
        protected void DrawDynamicSmoothLoop(float[] points, int pointCount, Vector4 color, float widthPixels)
            => DrawDynamicSmoothLoop(points, pointCount, ref _currentMvp, color, widthPixels);

        /// <inheritdoc cref="DrawDynamicSmoothLoop(float[], int, Vector4, float)"/>
        protected void DrawDynamicSmoothLoop(
            float[] points, int pointCount, ref Matrix4 mvp, Vector4 color, float widthPixels)
        {
            if (pointCount < 3)
                return;

            _joinedLines.Draw(
                PolylineRenderGeometry.BuildSegmentInstances(points, pointCount, closed: true),
                PolylineRenderGeometry.SegmentCount(pointCount, closed: true),
                PolylineRenderGeometry.BuildJoinInstances(points, pointCount, closed: true),
                PolylineRenderGeometry.JoinCount(pointCount, closed: true),
                ref mvp, color, widthPixels);
            GL.UseProgram(_shader);
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
        }

        // ── NDC conversion ────────────────────────────────────────────────────

        protected static (float nx, float ny) ScreenToNdc(float sx, float sy, float vpW, float vpH)
            => (sx / vpW * 2f - 1f, 1f - sy / vpH * 2f);

        // ── Screen-space render state helpers ─────────────────────────────────

        /// <summary>Switch to screen-space (NDC) rendering: identity MVP, depth disabled.</summary>
        protected void BeginScreenSpaceRender()
        {
            Matrix4 id = Matrix4.Identity;
            GL.UseProgram(_shader);
            SetMvp(ref id);
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

        // ── World-space 3-D cone arrow ────────────────────────────────────────

        /// <summary>
        /// Sets MVP to <paramref name="vp"/> and disables depth testing so subsequent
        /// <see cref="DrawWorldSpaceArrow"/> calls render on top of everything.
        /// Call <see cref="EndScreenSpaceRender"/> to restore depth state.
        /// </summary>
        protected void BeginWorldSpaceOverlay(ref Matrix4 vp)
        {
            GL.UseProgram(_shader);
            SetMvp(ref vp);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
        }

        /// <summary>
        /// Returns world-space units per screen pixel at <paramref name="worldPos"/> depth.
        /// Delegates to the camera's analytic Hilton math — no screen sampling.
        /// </summary>
        protected static float WorldUnitsPerPixel(Vector3 worldPos, OrbitCamera cam)
            => cam.WorldUnitsPerPixel(worldPos);

        /// <summary>
        /// Draws a 3-D arrow from <paramref name="start"/> to <paramref name="tip"/>.
        /// The shaft is rendered in screen-space (NDC) for exact zoom invariance.
        /// The cone is rendered in world-space for a 3-D look.
        /// Assumes depth test is disabled (call BeginWorldSpaceOverlay first).
        /// <paramref name="vp"/> is the current view*proj matrix (set by BeginWorldSpaceOverlay).
        /// </summary>
        protected void DrawWorldSpaceArrow(
            Vector3 start, Vector3 tip,
            float coneLen, float coneRad,
            Vector4 color, float lineWidth,
            OrbitCamera cam, ref Matrix4 vp)
        {
            Vector3 dir = tip - start;
            float   len = dir.Length;
            if (len < 1e-5f) return;
            dir /= len;

            Vector3 coneBase = tip - dir * coneLen;

            float vpW = cam.ViewportWidth;
            float vpH = cam.ViewportHeight;

            // Project shaft endpoints to screen space for direction-independent pixel length
            var (startSx, startSy, startBehind) = cam.WorldToScreen(start);
            var (cbSx,    cbSy,    cbBehind)    = cam.WorldToScreen(coneBase);

            // Shaft: screen-space line (exact pixel size regardless of arrow direction)
            if (!startBehind && !cbBehind)
            {
                var (snx, sny) = ScreenToNdc(startSx, startSy, vpW, vpH);
                var (cnx, cny) = ScreenToNdc(cbSx,    cbSy,    vpW, vpH);

                Matrix4 id = Matrix4.Identity;
                SetMvp(ref id);

                DrawLine(snx, sny, cnx, cny);
                DrawDynamicLines(0, 2, color with { W = 0.28f }, lineWidth + 3f);
                DrawDynamicLines(0, 2, color with { W = 0.95f }, lineWidth);

                // Restore VP matrix for world-space cone
                SetMvp(ref vp);
            }

            // Build two perpendicular vectors for the cone base circle
            Vector3 perp  = MathF.Abs(Vector3.Dot(dir, Vector3.UnitZ)) < 0.9f
                          ? Vector3.UnitZ : Vector3.UnitX;
            Vector3 right = Vector3.Cross(dir, perp).Normalized();
            Vector3 up    = Vector3.Cross(dir, right);

            for (int i = 0; i <= ConeSeg; i++)
            {
                float a = i * MathF.Tau / ConeSeg;
                _coneRing[i] = coneBase + (right * MathF.Cos(a) + up * MathF.Sin(a)) * coneRad;
            }

            // Cone sides: tip → ring edge pairs
            int ci = 0;
            for (int i = 0; i < ConeSeg; i++)
            {
                _coneBuf[ci++] = tip.X;               _coneBuf[ci++] = tip.Y;               _coneBuf[ci++] = tip.Z;
                _coneBuf[ci++] = _coneRing[i].X;      _coneBuf[ci++] = _coneRing[i].Y;      _coneBuf[ci++] = _coneRing[i].Z;
                _coneBuf[ci++] = _coneRing[i + 1].X;  _coneBuf[ci++] = _coneRing[i + 1].Y;  _coneBuf[ci++] = _coneRing[i + 1].Z;
            }
            Dyn(_coneBuf);
            SetColor(color with { W = 0.97f });
            GL.DrawArrays(PrimitiveType.Triangles, 0, ConeSeg * 3);

            // Cone cap disc (darker face for 3-D depth cue)
            ci = 0;
            for (int i = 0; i < ConeSeg; i++)
            {
                _capBuf[ci++] = coneBase.X;            _capBuf[ci++] = coneBase.Y;            _capBuf[ci++] = coneBase.Z;
                _capBuf[ci++] = _coneRing[i + 1].X;   _capBuf[ci++] = _coneRing[i + 1].Y;   _capBuf[ci++] = _coneRing[i + 1].Z;
                _capBuf[ci++] = _coneRing[i].X;       _capBuf[ci++] = _coneRing[i].Y;       _capBuf[ci++] = _coneRing[i].Z;
            }
            Dyn(_capBuf);
            SetColor(color.X * 0.50f, color.Y * 0.50f, color.Z * 0.50f, 0.90f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, ConeSeg * 3);
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
            DrawDynamicLines(0, 2, color with { W = 0.35f }, lineWidth + 3f);
            DrawDynamicLines(0, 2, color with { W = 1f }, lineWidth);

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
            SetMvp(ref mvp);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            for (int ax = 0; ax < 3; ax++)
            {
                Vector4 axisColor = AxisColor[ax] with { W = 0.65f };
                DrawLines(_axisVbo, ax * 2, 2, axisColor, 1.5f);
            }
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Abstract ──────────────────────────────────────────────────────────

        public abstract void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam);

        public virtual void Dispose()
        {
            _wideLines.Dispose();
            _joinedLines.Dispose();
            if (_shader  != -1) { GL.DeleteProgram(_shader);                                _shader  = -1; }
            if (_dynVao  != -1) { GL.DeleteVertexArray(_dynVao);  GL.DeleteBuffer(_dynVbo); _dynVao  = -1; }
            if (_axisVao != -1) { GL.DeleteVertexArray(_axisVao); GL.DeleteBuffer(_axisVbo); _axisVao = -1; }
        }
    }
}
