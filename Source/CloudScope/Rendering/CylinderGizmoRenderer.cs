using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Professional cylinder gizmo renderer — visual style matches Box/SphereGizmoRenderer.
    ///
    /// Layers:
    ///   1. Semi-transparent fill         (neutral blue, 7% alpha)
    ///   2. Axis diameter lines           (X=red, Y=green, Z=blue, 65% alpha)
    ///   3. Top + bottom cap circles      (cyan, depth-tested + ghost)
    ///   4. Vertical edge lines           (cyan, depth-tested + ghost)
    ///   5. Handle diamonds               (center=green, caps=white, radial=white, hover=yellow)
    /// </summary>
    public sealed class CylinderGizmoRenderer : GizmoRendererBase
    {
        private int _fillVao  = -1, _fillVbo  = -1;
        private int _circVao  = -1, _circVbo  = -1;

        private int _fillVertCount;
        private const int CapSeg  = 64;   // circle resolution
        private const int LatSeg  = 12;   // subdivisions along Y for fill mesh

        // ── Public entry point ────────────────────────────────────────────────

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            var cyl = (CylinderSelectionTool)tool;
            if (cyl.Radius < 1e-5f || cyl.HalfHeight < 1e-5f) return;

            EnsureResources();

            // Model: scale → rotate → translate
            Matrix4 model = Matrix4.CreateScale(cyl.Radius, cyl.HalfHeight, cyl.Radius)
                          * Matrix4.CreateFromQuaternion(cyl.Rotation)
                          * Matrix4.CreateTranslation(cyl.Center);
            Matrix4 mvp   = model * view * proj;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            RenderFill(mvp);
            RenderAxisLines(mvp);
            RenderWireframe(mvp);
            RenderHandles(cyl, cam);

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

        // ── Layer 3: Cap circles + vertical lines (depth + ghost) ─────────────

        private void RenderWireframe(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.BindVertexArray(_circVao);
            GL.DepthMask(false);

            // Two cap circles (top + bottom) + 4 vertical edge lines
            // Buffer layout: top circle [CapSeg*2], bottom circle [CapSeg*2], 4 vert lines [8]
            int capVerts  = CapSeg * 2;
            int totalVerts = capVerts * 2 + 8;

            // Depth pass
            GL.Enable(EnableCap.DepthTest);
            SetColor(0.25f, 0.85f, 0.95f, 0.85f);
            GL.LineWidth(1.8f);
            GL.DrawArrays(PrimitiveType.Lines, 0, totalVerts);

            // Ghost pass
            GL.Disable(EnableCap.DepthTest);
            SetColor(0.25f, 0.85f, 0.95f, 0.18f);
            GL.LineWidth(1.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, totalVerts);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 4: Handle diamonds (screen-space NDC) ───────────────────────

        private void RenderHandles(CylinderSelectionTool cyl, OrbitCamera cam)
        {
            float   vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            Matrix4 id  = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            for (int i = 0; i < cyl.HandleCount; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(cyl.HandleWorldPosition(i));
                if (behind) continue;

                var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                float hx = 12f / vpW, hy = 12f / vpH;

                Vector4 col = i == cyl.HoveredHandle
                    ? new(1f, 1f, 0.15f, 1f)
                    : i == 0
                        ? new(0.3f, 1f, 0.45f, 0.85f)   // center: green
                        : new(0.9f, 0.9f, 0.9f, 0.80f); // caps + radial: white

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

            // ── Fill mesh: side quads + top/bottom caps ────────────────────────
            // Side: LatSeg rings × CapSeg quads × 2 triangles × 3 verts
            // Caps: CapSeg triangles × 2 × 3 verts
            int sideVerts = LatSeg * CapSeg * 6;
            int capVerts  = CapSeg * 3 * 2;
            _fillVertCount = sideVerts + capVerts;
            float[] fill = new float[_fillVertCount * 3];
            int fi = 0;

            float step = MathF.Tau / CapSeg;

            // Side quads
            for (int la = 0; la < LatSeg; la++)
            {
                float y0 = -1f + 2f * la       / LatSeg;
                float y1 = -1f + 2f * (la + 1) / LatSeg;
                for (int s = 0; s < CapSeg; s++)
                {
                    float a0 = s * step, a1 = (s + 1) * step;
                    float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                    float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                    // Two triangles per quad (local: x=cos, y=Y, z=sin; scaled by model matrix)
                    fill[fi++]=c0; fill[fi++]=y0; fill[fi++]=s0;
                    fill[fi++]=c1; fill[fi++]=y0; fill[fi++]=s1;
                    fill[fi++]=c1; fill[fi++]=y1; fill[fi++]=s1;
                    fill[fi++]=c0; fill[fi++]=y0; fill[fi++]=s0;
                    fill[fi++]=c1; fill[fi++]=y1; fill[fi++]=s1;
                    fill[fi++]=c0; fill[fi++]=y1; fill[fi++]=s0;
                }
            }

            // Top cap (y=+1)
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                fill[fi++]=0f; fill[fi++]=1f; fill[fi++]=0f;
                fill[fi++]=MathF.Cos(a0); fill[fi++]=1f; fill[fi++]=MathF.Sin(a0);
                fill[fi++]=MathF.Cos(a1); fill[fi++]=1f; fill[fi++]=MathF.Sin(a1);
            }

            // Bottom cap (y=-1)
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                fill[fi++]=0f; fill[fi++]=-1f; fill[fi++]=0f;
                fill[fi++]=MathF.Cos(a1); fill[fi++]=-1f; fill[fi++]=MathF.Sin(a1);
                fill[fi++]=MathF.Cos(a0); fill[fi++]=-1f; fill[fi++]=MathF.Sin(a0);
            }

            MakeStaticVao(ref _fillVao, ref _fillVbo, fill);

            // ── Wireframe: top circle, bottom circle, 4 vertical lines ────────
            // top [CapSeg*2] + bottom [CapSeg*2] + 4 vert lines [8]
            float[] circ = new float[(CapSeg * 2 * 2 + 8) * 3];
            int ci = 0;

            // Top cap circle (y=+1)
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                circ[ci++]=MathF.Cos(a0); circ[ci++]=1f; circ[ci++]=MathF.Sin(a0);
                circ[ci++]=MathF.Cos(a1); circ[ci++]=1f; circ[ci++]=MathF.Sin(a1);
            }

            // Bottom cap circle (y=-1)
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                circ[ci++]=MathF.Cos(a0); circ[ci++]=-1f; circ[ci++]=MathF.Sin(a0);
                circ[ci++]=MathF.Cos(a1); circ[ci++]=-1f; circ[ci++]=MathF.Sin(a1);
            }

            // 4 vertical edge lines at 0°, 90°, 180°, 270°
            float[] angles = { 0f, MathF.PI * 0.5f, MathF.PI, MathF.PI * 1.5f };
            foreach (float a in angles)
            {
                float c = MathF.Cos(a), s2 = MathF.Sin(a);
                circ[ci++]=c; circ[ci++]= 1f; circ[ci++]=s2;
                circ[ci++]=c; circ[ci++]=-1f; circ[ci++]=s2;
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
