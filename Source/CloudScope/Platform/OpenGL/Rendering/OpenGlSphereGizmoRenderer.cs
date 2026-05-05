using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Professional sphere gizmo renderer — visual style matches OpenGlBoxGizmoRenderer.
    ///
    /// Layers:
    ///   1. Semi-transparent sphere fill   (neutral blue, 7% alpha)
    ///   2. Axis diameter lines            (X=red, Y=green, Z=blue, 65% alpha)
    ///   3. Three great-circle rings       (axis-colored, depth-tested + ghost)
    ///   4. Handle diamonds                (center=green, poles=white, hover=yellow)
    /// </summary>
    public sealed class OpenGlSphereGizmoRenderer : OpenGlGizmoRendererBase
    {
        private int _fillVao = -1, _fillVbo = -1;
        private int _circVao = -1, _circVbo = -1;

        private int _fillVertCount;
        private const int Seg = 64;
        private const int Lat = 16;
        private const int Lon = 32;

        // ── Public entry point ────────────────────────────────────────────────

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            var sphere = (SphereSelectionTool)tool;
            if (sphere.Radius < 1e-5f) return;

            EnsureResources();

            Matrix4 model = Matrix4.CreateScale(sphere.Radius) * Matrix4.CreateTranslation(sphere.Center);
            Matrix4 mvp   = model * view * proj;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            RenderFill(mvp);
            RenderAxisLines(mvp);
            RenderCircles(mvp);
            RenderHandles(sphere, cam);

            GL.Disable(EnableCap.Blend);
        }

        // ── Layer 1: Transparent fill ─────────────────────────────────────────

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

        // ── Layer 3: Great-circle rings (depth + ghost) ───────────────────────

        private void RenderCircles(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.BindVertexArray(_circVao);
            GL.DepthMask(false);

            for (int ax = 0; ax < 3; ax++)
            {
                var c      = AxisColor[ax];
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

        private void RenderHandles(SphereSelectionTool sphere, OrbitCamera cam)
        {
            float   vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            Matrix4 id  = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            for (int i = 0; i < sphere.HandleCount; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(sphere.HandleWorldPosition(i));
                if (behind) continue;

                var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                float hx = 12f/vpW, hy = 12f/vpH;

                Vector4 col = i == sphere.HoveredHandle
                    ? new(1f, 1f, 0.15f, 1f)
                    : i == 0
                        ? new(0.3f, 1f, 0.45f, 0.85f)
                        : new(0.9f, 0.9f, 0.9f, 0.80f);

                DrawDiamond(nx, ny, hx, hy, col);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Resource init ─────────────────────────────────────────────────────

        private void EnsureResources()
        {
            EnsureBaseResources();
            if (_fillVao != -1) return;

            // UV-sphere fill
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
                    void P(float cp, float sp, float ct, float st)
                    { fill[fi++] = cp*ct; fill[fi++] = sp; fill[fi++] = cp*st; }
                    P(cp0,sp0,ct0,st0); P(cp1,sp1,ct0,st0); P(cp1,sp1,ct1,st1);
                    P(cp0,sp0,ct0,st0); P(cp1,sp1,ct1,st1); P(cp0,sp0,ct1,st1);
                }
            }
            MakeStaticVao(ref _fillVao, ref _fillVbo, fill);

            // 3 great-circle rings
            float[] circ = new float[3 * Seg * 2 * 3];
            int ci = 0;
            float step = MathF.Tau / Seg;
            for (int s = 0; s < Seg; s++)
            {
                float a1 = s * step, a2 = (s + 1) * step;
                circ[ci++]=0f; circ[ci++]=MathF.Cos(a1); circ[ci++]=MathF.Sin(a1);
                circ[ci++]=0f; circ[ci++]=MathF.Cos(a2); circ[ci++]=MathF.Sin(a2);
            }
            for (int s = 0; s < Seg; s++)
            {
                float a1 = s * step, a2 = (s + 1) * step;
                circ[ci++]=MathF.Cos(a1); circ[ci++]=0f; circ[ci++]=MathF.Sin(a1);
                circ[ci++]=MathF.Cos(a2); circ[ci++]=0f; circ[ci++]=MathF.Sin(a2);
            }
            for (int s = 0; s < Seg; s++)
            {
                float a1 = s * step, a2 = (s + 1) * step;
                circ[ci++]=MathF.Cos(a1); circ[ci++]=MathF.Sin(a1); circ[ci++]=0f;
                circ[ci++]=MathF.Cos(a2); circ[ci++]=MathF.Sin(a2); circ[ci++]=0f;
            }
            MakeStaticVao(ref _circVao, ref _circVbo, circ);
        }

        public override void Dispose()
        {
            if (_fillVao != -1) { GL.DeleteVertexArray(_fillVao); GL.DeleteBuffer(_fillVbo); _fillVao = -1; }
            if (_circVao != -1) { GL.DeleteVertexArray(_circVao); GL.DeleteBuffer(_circVbo); _circVao = -1; }
            base.Dispose();
        }
    }
}
