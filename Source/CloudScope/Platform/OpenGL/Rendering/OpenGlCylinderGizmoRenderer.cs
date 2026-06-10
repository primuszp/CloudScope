using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Professional cylinder gizmo renderer — visual style matches OpenGlBoxGizmoRenderer.
    ///
    /// Drawing phase : live circle preview on screen
    /// Editing phase :
    ///   1. Semi-transparent fill         (blue, 7% alpha)
    ///   2. Shared axis lines             (X/Y/Z from base)
    ///   3. Cap circles + vertical edges  (cyan, depth + ghost)
    ///   4. Extrude arrow                 (orange, on top cap)
    ///   5. Rotation rings                (R/G/B, screen-space)
    ///   6. Handle diamonds               (center=green, others=white, hover=yellow)
    /// </summary>
    public sealed class OpenGlCylinderGizmoRenderer : OpenGlGizmoRendererBase
    {
        private int _fillVao = -1, _fillVbo = -1;
        private int _circVao = -1, _circVbo = -1;

        private int _fillVertCount;
        private const int CapSeg = 64;
        private const int LatSeg = 12;

        // ── Public entry point ────────────────────────────────────────────────

        public override void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            var cyl = (CylinderSelectionTool)tool;

            EnsureResources();

            if (cyl.Phase == ToolPhase.Drawing)
            {
                RenderDrawingCircle(cyl, cam);
                return;
            }

            if (cyl.Radius < 1e-5f || cyl.HalfHeight < 1e-5f) return;

            Matrix4 model = Matrix4.CreateScale(cyl.Radius, cyl.Radius, cyl.HalfHeight)
                          * Matrix4.CreateFromQuaternion(cyl.Rotation)
                          * Matrix4.CreateTranslation(cyl.Center);
            Matrix4 mvp = model * view * proj;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            Matrix4 vp = view * proj;

            RenderFill(mvp);
            RenderAxisLines(mvp);
            RenderWireframe(mvp);
            RenderHeightArrows(cyl, cam, vp);
            RenderRadiusArrows(cyl, cam, vp);
            RenderRings(cyl, cam);
            RenderHandles(cyl, cam);

            GL.Disable(EnableCap.Blend);
        }

        // ── Drawing overlay: live circle while dragging ───────────────────────

        private void RenderDrawingCircle(CylinderSelectionTool cyl, OrbitCamera cam)
        {
            if (cyl.Radius < 1e-5f) return;

            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            var (cx, cy, behind) = cam.WorldToScreen(cyl.Center);
            if (behind) return;

            const int N = 64;
            int vc = 0;
            float pnx = 0, pny = 0; bool pok = false;

            for (int j = 0; j <= N; j++)
            {
                float t = j * MathF.Tau / N;
                Vector3 pt = cyl.Center + new Vector3(MathF.Cos(t) * cyl.Radius, MathF.Sin(t) * cyl.Radius, 0f);
                var (sx, sy, beh) = cam.WorldToScreen(pt);
                var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                if (pok && !beh)
                {
                    if (vc + 6 > _ringSegBuf.Length) Array.Resize(ref _ringSegBuf, _ringSegBuf.Length * 2);
                    _ringSegBuf[vc++] = pnx; _ringSegBuf[vc++] = pny; _ringSegBuf[vc++] = 0f;
                    _ringSegBuf[vc++] = nx;  _ringSegBuf[vc++] = ny;  _ringSegBuf[vc++] = 0f;
                }
                pnx = nx; pny = ny; pok = !beh;
            }
            if (vc == 0) return;

            BeginScreenSpaceRender();
            Dyn(_ringSegBuf, vc);
            SetColor(0.25f, 0.85f, 0.95f, 0.90f);
            GL.LineWidth(2f);
            GL.DrawArrays(PrimitiveType.Lines, 0, vc / 3);
            var (cnx, cny) = ScreenToNdc(cx, cy, vpW, vpH);
            float hx = 6f / vpW, hy = 6f / vpH;
            DrawDiamondFill(cnx, cny, hx, hy, new(0.3f, 1f, 0.45f, 0.9f));
            EndScreenSpaceRender();
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

        // ── Layer 3: Cap circles + vertical edges (depth + ghost) ─────────────

        private void RenderWireframe(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.BindVertexArray(_circVao);
            GL.DepthMask(false);

            int capVerts   = CapSeg * 2;
            int totalVerts = capVerts * 2 + 8;

            GL.Enable(EnableCap.DepthTest);
            SetColor(0.25f, 0.85f, 0.95f, 0.85f);
            GL.LineWidth(1.8f);
            GL.DrawArrays(PrimitiveType.Lines, 0, totalVerts);

            GL.Disable(EnableCap.DepthTest);
            SetColor(0.25f, 0.85f, 0.95f, 0.18f);
            GL.LineWidth(1.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, totalVerts);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 4: Height arrows (top cap ↑ and bottom cap ↓) ──────────────

        private void RenderHeightArrows(CylinderSelectionTool cyl, OrbitCamera cam, Matrix4 vp)
        {
            float wpp     = WorldUnitsPerPixel(cyl.Center, cam);
            float coneLen = GripArrowSupport.ConeHeightPixels * wpp;
            float coneRad = GripArrowSupport.ConeRadiusPixels * wpp;

            BeginWorldSpaceOverlay(ref vp);

            for (int gripIdx = 1; gripIdx <= 2; gripIdx++)
            {
                GripDescriptor grip  = cyl.GetGrip(gripIdx);
                float totLen = grip.IsPrimary
                    ? GripArrowSupport.ArrowHeightPixels * 1.2f
                    : GripArrowSupport.ArrowHeightPixels;
                GripArrow3D arrow = GripArrowSupport.CreateScreenSized(grip, cam, totLen);

                GripVisualDescriptor style = GripVisualStyleResolver.ResolveAxisGrip(
                    grip,
                    gripIdx == cyl.HoveredHandle,
                    emphasizePrimary: true,
                    AxisColor[grip.Axis],
                    gripIdx == cyl.ActiveHandle);

                DrawWorldSpaceArrow(arrow.Start, arrow.Tip, coneLen, coneRad, style.Color, MathF.Max(style.LineWidth, 2.5f));
            }

            EndScreenSpaceRender();
        }

        // ── Layer 4b: Radius arrows (±LocalX, ±LocalY) ───────────────────────

        private void RenderRadiusArrows(CylinderSelectionTool cyl, OrbitCamera cam, Matrix4 vp)
        {
            float wpp     = WorldUnitsPerPixel(cyl.Center, cam);
            float coneLen = GripArrowSupport.ConeHeightPixels * wpp;
            float coneRad = GripArrowSupport.ConeRadiusPixels * wpp;

            BeginWorldSpaceOverlay(ref vp);

            foreach (GripDescriptor grip in cyl.Grips)
            {
                if (grip.Kind != GripKind.RadiusResize) continue;

                int         i     = grip.Index;
                GripArrow3D arrow = GripArrowSupport.CreateScreenSized(grip, cam);

                GripVisualDescriptor style = GripVisualStyleResolver.ResolveAxisGrip(
                    grip,
                    i == cyl.HoveredHandle,
                    emphasizePrimary: false,
                    AxisColor[grip.Axis],
                    i == cyl.ActiveHandle);

                DrawWorldSpaceArrow(arrow.Start, arrow.Tip, coneLen, coneRad, style.Color, MathF.Max(style.LineWidth, 2f));
            }

            EndScreenSpaceRender();
        }

        // ── Layer 5: Rotation rings ───────────────────────────────────────────

        private void RenderRings(CylinderSelectionTool cyl, OrbitCamera cam)
        {
            const int N    = 64;
            float     rad  = cyl.RingRadius;
            Matrix3   invR = Matrix3.Transpose(Matrix3.CreateFromQuaternion(cyl.Rotation));
            float     vpW  = cam.ViewportWidth, vpH = cam.ViewportHeight;

            BeginScreenSpaceRender();

            for (int axis = 0; axis < 3; axis++)
            {
                bool hov = cyl.TryGetGrip(cyl.HoveredHandle, out GripDescriptor hoveredGrip)
                    && hoveredGrip.Kind == GripKind.RotationRing
                    && hoveredGrip.Axis == axis;
                bool active = cyl.TryGetGrip(cyl.ActiveHandle, out GripDescriptor activeGrip)
                    && activeGrip.Kind == GripKind.RotationRing
                    && activeGrip.Axis == axis;
                GripVisualDescriptor style = GripVisualStyleResolver.ResolveRing(hov, AxisColor[axis], active);
                GL.LineWidth(style.LineWidth);

                int   vc  = 0;
                float psx = 0, psy = 0;
                bool  pok = false;

                for (int j = 0; j <= N; j++)
                {
                    float t = j * MathF.Tau / N;
                    float ct = MathF.Cos(t), st = MathF.Sin(t);
                    Vector3 local = axis switch
                    {
                        0 => new Vector3(0f, ct, st),
                        1 => new Vector3(ct, 0f, st),
                        _ => new Vector3(ct, st, 0f),
                    } * rad;

                    var (sx, sy, behind) = cam.WorldToScreen(cyl.Center + invR * local);
                    var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                    if (pok && !behind)
                    {
                        if (vc + 6 > _ringSegBuf.Length) Array.Resize(ref _ringSegBuf, _ringSegBuf.Length * 2);
                        _ringSegBuf[vc++] = psx; _ringSegBuf[vc++] = psy; _ringSegBuf[vc++] = 0f;
                        _ringSegBuf[vc++] = nx;  _ringSegBuf[vc++] = ny;  _ringSegBuf[vc++] = 0f;
                    }
                    psx = nx; psy = ny; pok = !behind;
                }
                if (vc == 0) continue;
                Dyn(_ringSegBuf, vc);
                SetColor(style.Color);
                GL.DrawArrays(PrimitiveType.Lines, 0, vc / 3);
            }

            EndScreenSpaceRender();
        }

        // ── Layer 6: Center handle diamond ───────────────────────────────────

        private void RenderHandles(CylinderSelectionTool cyl, OrbitCamera cam)
        {
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            BeginScreenSpaceRender();

            GripDescriptor center = cyl.GetGrip(0);
            var (sx, sy, behind) = cam.WorldToScreen(center.Position);
            if (!behind)
            {
                var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                float hx = 12f / vpW, hy = 12f / vpH;
                GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(
                    center, 0 == cyl.HoveredHandle, 0 == cyl.ActiveHandle);
                DrawDiamond(nx, ny, hx, hy, style.Color);
            }

            EndScreenSpaceRender();
        }

        // ── Resource init ─────────────────────────────────────────────────────

        private void EnsureResources()
        {
            EnsureBaseResources();
            if (_fillVao != -1) return;

            int sideVerts  = LatSeg * CapSeg * 6;
            int capVerts   = CapSeg * 3 * 2;
            _fillVertCount = sideVerts + capVerts;
            float[] fill   = new float[_fillVertCount * 3];
            int fi = 0;
            float step = MathF.Tau / CapSeg;

            for (int la = 0; la < LatSeg; la++)
            {
                float z0 = -1f + 2f * la       / LatSeg;
                float z1 = -1f + 2f * (la + 1) / LatSeg;
                for (int s = 0; s < CapSeg; s++)
                {
                    float a0 = s * step, a1 = (s + 1) * step;
                    float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                    float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                    fill[fi++]=c0; fill[fi++]=s0; fill[fi++]=z0;
                    fill[fi++]=c1; fill[fi++]=s1; fill[fi++]=z0;
                    fill[fi++]=c1; fill[fi++]=s1; fill[fi++]=z1;
                    fill[fi++]=c0; fill[fi++]=s0; fill[fi++]=z0;
                    fill[fi++]=c1; fill[fi++]=s1; fill[fi++]=z1;
                    fill[fi++]=c0; fill[fi++]=s0; fill[fi++]=z1;
                }
            }
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                fill[fi++]=0f; fill[fi++]=0f; fill[fi++]=1f;
                fill[fi++]=MathF.Cos(a0); fill[fi++]=MathF.Sin(a0); fill[fi++]=1f;
                fill[fi++]=MathF.Cos(a1); fill[fi++]=MathF.Sin(a1); fill[fi++]=1f;
            }
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                fill[fi++]=0f; fill[fi++]=0f; fill[fi++]=-1f;
                fill[fi++]=MathF.Cos(a1); fill[fi++]=MathF.Sin(a1); fill[fi++]=-1f;
                fill[fi++]=MathF.Cos(a0); fill[fi++]=MathF.Sin(a0); fill[fi++]=-1f;
            }
            MakeStaticVao(ref _fillVao, ref _fillVbo, fill);

            float[] circ = new float[(CapSeg * 2 * 2 + 8) * 3];
            int ci = 0;
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                circ[ci++]=MathF.Cos(a0); circ[ci++]=MathF.Sin(a0); circ[ci++]= 1f;
                circ[ci++]=MathF.Cos(a1); circ[ci++]=MathF.Sin(a1); circ[ci++]= 1f;
            }
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                circ[ci++]=MathF.Cos(a0); circ[ci++]=MathF.Sin(a0); circ[ci++]=-1f;
                circ[ci++]=MathF.Cos(a1); circ[ci++]=MathF.Sin(a1); circ[ci++]=-1f;
            }
            float[] edgeAngles = { 0f, MathF.PI * 0.5f, MathF.PI, MathF.PI * 1.5f };
            foreach (float a in edgeAngles)
            {
                float c = MathF.Cos(a), s2 = MathF.Sin(a);
                circ[ci++]=c; circ[ci++]=s2; circ[ci++]= 1f;
                circ[ci++]=c; circ[ci++]=s2; circ[ci++]=-1f;
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
