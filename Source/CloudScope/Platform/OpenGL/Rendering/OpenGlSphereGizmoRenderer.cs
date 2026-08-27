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
    internal sealed class OpenGlSphereGizmoRenderer : OpenGlGizmoRendererBase
    {
        private int _fillVao = -1, _fillVbo = -1;
        private int _circVao = -1, _circVbo = -1;
        private readonly OpenGlSmoothPolylineRenderer _smoothCircles = new();

        private int _fillVertCount;
        // At 512 sides the projected-circle deviation stays well below one pixel even on
        // Retina/4K displays. This is cheap: rings are instanced screen-space quads.
        private const int Seg = 512;
        private const int Lat = 16;
        private const int Lon = 32;
        private const int CircleVertexCount = (Seg + 1) * 2;

        // ── Public entry point ────────────────────────────────────────────────

        public override void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            var sphere = (SphereSelectionTool)tool;
            if (sphere.Radius < 1e-5f) return;

            EnsureResources();

            Matrix4 model = Matrix4.CreateScale(sphere.Radius) * Matrix4.CreateTranslation(sphere.Center);
            Matrix4 mvp   = model * view * proj;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            Matrix4 vp = view * proj;

            RenderFill(mvp);
            RenderAxisLines(mvp);
            RenderCircles(mvp);
            RenderHandles(sphere, cam, vp);

            GL.Disable(EnableCap.Blend);
        }

        // ── Layer 1: Transparent fill ─────────────────────────────────────────

        private void RenderFill(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            SetMvp(ref mvp);
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
            GL.DepthMask(false);

            for (int ax = 0; ax < 3; ax++)
            {
                var c      = AxisColor[ax];
                int offset = ax * CircleVertexCount;

                GL.Enable(EnableCap.DepthTest);
                _smoothCircles.Draw(_circVbo, offset, CircleVertexCount, ref mvp, c with { W = 0.80f }, 2.0f);

                GL.Disable(EnableCap.DepthTest);
                _smoothCircles.Draw(_circVbo, offset, CircleVertexCount, ref mvp, c with { W = 0.18f }, 1.0f);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 4: Radius arrows + center diamond ───────────────────────────

        private void RenderHandles(SphereSelectionTool sphere, OrbitCamera cam, Matrix4 vp)
        {
            // World-space pass: arrows for all radius-resize poles
            BeginWorldSpaceOverlay(ref vp);

            foreach (GripDescriptor grip in sphere.Grips)
            {
                if (grip.Kind != GripKind.RadiusResize) continue;
                if (!sphere.IsGripVisible(grip.Index)) continue;

                int         i        = grip.Index;
                float       arrowLen = sphere.AdaptiveArrowLength(grip, cam);
                float       coneLen  = arrowLen * GripArrowSupport.ConeToArrowRatio;
                float       coneRad  = arrowLen * GripArrowSupport.ConeRadiusToArrow;
                GripArrow3D arrow    = GripArrowSupport.Create(grip, arrowLen);

                GripVisualDescriptor style = GripVisualStyleResolver.ResolveAxisGrip(
                    grip,
                    i == sphere.HoveredHandle,
                    emphasizePrimary: false,
                    AxisColor[grip.Axis],
                    i == sphere.ActiveHandle);

                DrawWorldSpaceArrow(arrow.Start, arrow.Tip, coneLen, coneRad, style.Color, MathF.Max(style.LineWidth, 2f), cam, ref vp);
            }

            EndScreenSpaceRender();

            // Screen-space pass: center handle diamond
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            GripDescriptor center = sphere.GetGrip(0);
            var (sx, sy, behind) = cam.WorldToScreen(center.Position);
            if (!behind)
            {
                BeginScreenSpaceRender();
                var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                float hx = 12f / vpW, hy = 12f / vpH;
                GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(
                    center, 0 == sphere.HoveredHandle, 0 == sphere.ActiveHandle);
                DrawDiamond(nx, ny, hx, hy, style.Color);
                EndScreenSpaceRender();
            }
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
            float[] circ = new float[3 * CircleVertexCount * 9];
            int ci = 0;
            AddCircleLoop(circ, ref ci, 0);
            AddCircleLoop(circ, ref ci, 1);
            AddCircleLoop(circ, ref ci, 2);
            MakeStaticVao(ref _circVao, ref _circVbo, circ);
        }

        private static void AddCircleLoop(float[] data, ref int i, int axis)
        {
            for (int point = 0; point <= Seg; point++)
            {
                int previous = (point + Seg - 1) % Seg;
                int current = point % Seg;
                int next = (point + 1) % Seg;
                for (int side = 0; side < 2; side++)
                {
                    AddCirclePoint(data, ref i, axis, previous);
                    AddCirclePoint(data, ref i, axis, current);
                    AddCirclePoint(data, ref i, axis, next);
                }
            }
        }

        private static void AddCirclePoint(float[] data, ref int i, int axis, int point)
        {
            float angle = point * MathF.Tau / Seg;
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            if (axis == 0) { data[i++] = 0f; data[i++] = c; data[i++] = s; }
            else if (axis == 1) { data[i++] = c; data[i++] = 0f; data[i++] = s; }
            else { data[i++] = c; data[i++] = s; data[i++] = 0f; }
        }

        public override void Dispose()
        {
            if (_fillVao != -1) { GL.DeleteVertexArray(_fillVao); GL.DeleteBuffer(_fillVbo); _fillVao = -1; }
            if (_circVao != -1) { GL.DeleteVertexArray(_circVao); GL.DeleteBuffer(_circVbo); _circVao = -1; }
            _smoothCircles.Dispose();
            base.Dispose();
        }
    }
}
