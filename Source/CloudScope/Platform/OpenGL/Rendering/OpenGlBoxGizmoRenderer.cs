using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Professional OBB gizmo renderer.
    ///
    /// Drawing phase  : 2D rubber-band overlay
    /// Editing phase  : layered rendering
    ///   1. Semi-transparent face fills  (axis-tinted)
    ///   2. Local axis lines X/Y/Z
    ///   3. Wireframe                    (cyan, depth + ghost)
    ///   4. Face arrow handles           (axis-colored)
    ///   5. Corner + center handles      (white / green diamonds)
    ///   6. Rotation rings               (R/G/B)
    ///   7. Extrude arrow                (orange, flat box only)
    /// </summary>
    public sealed class OpenGlBoxGizmoRenderer : OpenGlGizmoRendererBase, IBoxSelectionGizmoRenderer
    {
        // ── Static GL resources ───────────────────────────────────────────────
        private int _edgeVao = -1, _edgeVbo = -1;
        private int _faceVao = -1, _faceVbo = -1;

        private static readonly float[] Edges =
        {
            -1,-1,-1,  1,-1,-1,   1,-1,-1,  1, 1,-1,
             1, 1,-1, -1, 1,-1,  -1, 1,-1, -1,-1,-1,
            -1,-1, 1,  1,-1, 1,   1,-1, 1,  1, 1, 1,
             1, 1, 1, -1, 1, 1,  -1, 1, 1, -1,-1, 1,
            -1,-1,-1, -1,-1, 1,   1,-1,-1,  1,-1, 1,
             1, 1,-1,  1, 1, 1,  -1, 1,-1, -1, 1, 1,
        };

        private static readonly float[] Faces =
        {
            -1,-1,-1, -1, 1,-1, -1, 1, 1,   -1,-1,-1, -1, 1, 1, -1,-1, 1, // -X
             1,-1,-1,  1, 1,-1,  1, 1, 1,    1,-1,-1,  1, 1, 1,  1,-1, 1, // +X
            -1,-1,-1,  1,-1,-1,  1,-1, 1,   -1,-1,-1,  1,-1, 1, -1,-1, 1, // -Y
            -1, 1,-1,  1, 1,-1,  1, 1, 1,   -1, 1,-1,  1, 1, 1, -1, 1, 1, // +Y
            -1,-1,-1,  1,-1,-1,  1, 1,-1,   -1,-1,-1,  1, 1,-1, -1, 1,-1, // -Z
            -1,-1, 1,  1,-1, 1,  1, 1, 1,   -1,-1, 1,  1, 1, 1, -1, 1, 1, // +Z
        };

        private static readonly Vector4[] FaceFillColor =
        {
            new(0.95f, 0.20f, 0.20f, 0.07f),  // -X
            new(0.95f, 0.20f, 0.20f, 0.07f),  // +X
            new(0.20f, 0.95f, 0.30f, 0.07f),  // -Y
            new(0.20f, 0.95f, 0.30f, 0.07f),  // +Y
            new(0.25f, 0.55f, 1.00f, 0.07f),  // -Z
            new(0.25f, 0.55f, 1.00f, 0.07f),  // +Z
        };

        // ── Public entry points ───────────────────────────────────────────────

        public override void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            var box = (BoxSelectionTool)tool;
            EnsureResources();
            Matrix4 model = box.GetModelMatrix();
            Matrix4 mvp   = model * view * proj;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            Matrix4 vp = view * proj;

            RenderFaceFills(mvp);
            RenderAxisLines(mvp);
            RenderWireframe(mvp);
            RenderFaceArrows(box, cam, vp);
            RenderCornerHandles(box, cam);
            RenderRings(box, cam);
            GL.Disable(EnableCap.Blend);
        }

        public void RenderPlacementRect(IRenderFrameData frameData, int x0, int y0, int x1, int y1, int vpW, int vpH)
        {
            EnsureResources();
            float nx0 = Math.Min(x0, x1) / (float)vpW * 2f - 1f;
            float nx1 = Math.Max(x0, x1) / (float)vpW * 2f - 1f;
            float ny0 = 1f - Math.Max(y0, y1) / (float)vpH * 2f;
            float ny1 = 1f - Math.Min(y0, y1) / (float)vpH * 2f;
            if (Math.Abs(nx1 - nx0) < 0.002f && Math.Abs(ny1 - ny0) < 0.002f) return;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            BeginScreenSpaceRender();

            SetColor(0f, 0.78f, 1f, 0.12f);
            Dyn(new[]{ nx0,ny0,0f, nx1,ny0,0f, nx1,ny1,0f, nx0,ny0,0f, nx1,ny1,0f, nx0,ny1,0f });
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            SetColor(0f, 0.82f, 1f, 0.85f); GL.LineWidth(1.5f);
            Dyn(new[]{ nx0,ny0,0f, nx1,ny0,0f, nx1,ny0,0f, nx1,ny1,0f, nx1,ny1,0f, nx0,ny1,0f, nx0,ny1,0f, nx0,ny0,0f });
            GL.DrawArrays(PrimitiveType.Lines, 0, 8);

            float d = Math.Max(Math.Min(Math.Abs(nx1 - nx0), Math.Abs(ny1 - ny0)) * 0.18f, 0.008f);
            SetColor(1f, 1f, 1f, 0.95f); GL.LineWidth(2f);
            Dyn(new[]{
                nx0,ny0,0f, nx0+d,ny0,0f,  nx0,ny0,0f, nx0,ny0+d,0f,
                nx1,ny0,0f, nx1-d,ny0,0f,  nx1,ny0,0f, nx1,ny0+d,0f,
                nx1,ny1,0f, nx1-d,ny1,0f,  nx1,ny1,0f, nx1,ny1-d,0f,
                nx0,ny1,0f, nx0+d,ny1,0f,  nx0,ny1,0f, nx0,ny1-d,0f,
            });
            GL.DrawArrays(PrimitiveType.Lines, 0, 16);

            EndScreenSpaceRender();
            GL.Disable(EnableCap.Blend);
        }

        // ── Layer 1: Face fills ───────────────────────────────────────────────

        private void RenderFaceFills(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.BindVertexArray(_faceVao);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);
            for (int f = 0; f < 6; f++)
            {
                SetColor(FaceFillColor[f]);
                GL.DrawArrays(PrimitiveType.Triangles, f * 6, 6);
            }
            GL.DepthMask(true);
        }

        // ── Layer 3: Wireframe ────────────────────────────────────────────────

        private void RenderWireframe(Matrix4 mvp)
        {
            GL.UseProgram(_shader);
            GL.BindVertexArray(_edgeVao);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            GL.DepthMask(false);

            GL.Enable(EnableCap.DepthTest);
            SetColor(0f, 0.8f, 1f, 0.65f);
            GL.LineWidth(1.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);

            GL.Disable(EnableCap.DepthTest);
            SetColor(0f, 0.8f, 1f, 0.12f);
            GL.LineWidth(1f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 4: Face arrow handles ───────────────────────────────────────

        private void RenderFaceArrows(BoxSelectionTool box, OrbitCamera cam, Matrix4 vp)
        {
            float wpp     = WorldUnitsPerPixel(box.Center, cam);
            float coneLen = GripArrowSupport.ConeHeightPixels * wpp;
            float coneRad = GripArrowSupport.ConeRadiusPixels * wpp;

            BeginWorldSpaceOverlay(ref vp);

            foreach (GripDescriptor grip in box.Grips)
            {
                if (grip.Kind != GripKind.AxisResize || grip.Axis is < 0 or > 2)
                    continue;

                int         i      = grip.Index;
                float       totLen = grip.IsPrimary && box.IsFlat
                    ? GripArrowSupport.ArrowHeightPixels * 1.3f
                    : GripArrowSupport.ArrowHeightPixels;
                GripArrow3D arrow  = GripArrowSupport.CreateScreenSized(grip, cam, totLen);

                GripVisualDescriptor style = GripVisualStyleResolver.ResolveAxisGrip(
                    grip,
                    i == box.HoveredHandle,
                    box.IsFlat,
                    AxisColor[grip.Axis],
                    i == box.ActiveHandle);

                DrawWorldSpaceArrow(arrow.Start, arrow.Tip, coneLen, coneRad, style.Color, MathF.Max(style.LineWidth, 2f));
            }

            EndScreenSpaceRender();
        }

        // ── Layer 5: Corner + center handles ─────────────────────────────────

        private void RenderCornerHandles(BoxSelectionTool box, OrbitCamera cam)
        {
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            BeginScreenSpaceRender();

            foreach (GripDescriptor grip in box.Grips)
            {
                if (grip.Kind != GripKind.CornerResize && grip.Kind != GripKind.Center)
                    continue;

                int i = grip.Index;
                var (sx, sy, behind) = cam.WorldToScreen(grip.Position);
                if (behind) continue;
                var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                float hx = 12f/vpW, hy = 12f/vpH;

                GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(
                    grip,
                    i == box.HoveredHandle,
                    i == box.ActiveHandle);
                DrawDiamond(nx, ny, hx, hy, style.Color);
            }

            EndScreenSpaceRender();
        }

        // ── Layer 6: Rotation rings ───────────────────────────────────────────

        private void RenderRings(BoxSelectionTool box, OrbitCamera cam)
        {
            const int N      = 64;
            float     radius = box.RingRadius;
            Matrix3   invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            float     vpW    = cam.ViewportWidth, vpH = cam.ViewportHeight;

            BeginScreenSpaceRender();

            for (int axis = 0; axis < 3; axis++)
            {
                bool hov = box.TryGetGrip(box.HoveredHandle, out GripDescriptor hoveredGrip)
                    && hoveredGrip.Kind == GripKind.RotationRing
                    && hoveredGrip.Axis == axis;
                bool active = box.TryGetGrip(box.ActiveHandle, out GripDescriptor activeGrip)
                    && activeGrip.Kind == GripKind.RotationRing
                    && activeGrip.Axis == axis;
                GripVisualDescriptor style = GripVisualStyleResolver.ResolveRing(hov, AxisColor[axis], active);
                GL.LineWidth(style.LineWidth);

                int vc = 0;
                float psx = 0, psy = 0; bool pok = false;
                for (int j = 0; j <= N; j++)
                {
                    float t  = j * MathF.Tau / N;
                    float ct = MathF.Cos(t), st = MathF.Sin(t);
                    Vector3 local = axis switch
                    {
                        0 => new Vector3(0, ct, st),
                        1 => new Vector3(ct, 0, st),
                        _ => new Vector3(ct, st, 0),
                    } * radius;

                    var (sx, sy, behind) = cam.WorldToScreen(box.Center + invRot * local);
                    var (nx, ny) = ScreenToNdc(sx, sy, vpW, vpH);
                    if (pok && !behind)
                    {
                        if (vc + 6 > _ringSegBuf.Length) Array.Resize(ref _ringSegBuf, _ringSegBuf.Length * 2);
                        _ringSegBuf[vc++]=psx; _ringSegBuf[vc++]=psy; _ringSegBuf[vc++]=0f;
                        _ringSegBuf[vc++]=nx;  _ringSegBuf[vc++]=ny;  _ringSegBuf[vc++]=0f;
                    }
                    psx=nx; psy=ny; pok=!behind;
                }
                if (vc == 0) continue;
                Dyn(_ringSegBuf, vc);
                SetColor(style.Color);
                GL.DrawArrays(PrimitiveType.Lines, 0, vc / 3);
            }

            EndScreenSpaceRender();
        }

        // ── Layer 7: Extrude arrow (flat box only) ────────────────────────────

        private void RenderExtrudeArrow(BoxSelectionTool box, OrbitCamera cam)
        {
            GripDescriptor grip = box.GetGrip(BoxSelectionTool.ExtrudeHandle);
            GripArrow3D arrow = GripArrowSupport.Create(grip, box.ArrowLength(grip));

            var (fx, fy, fb) = cam.WorldToScreen(arrow.Start);
            var (tx, ty, tb) = cam.WorldToScreen(arrow.Tip);
            if (fb || tb) return;

            float   vpW    = cam.ViewportWidth, vpH = cam.ViewportHeight;
            GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(
                grip,
                box.HoveredHandle == grip.Index,
                box.ActiveHandle  == grip.Index);

            BeginScreenSpaceRender();

            DrawProfessionalArrow(fx, fy, tx, ty, vpW, vpH, style.Color, MathF.Max(style.LineWidth, 3f));

            EndScreenSpaceRender();
        }

        // ── Resource init ─────────────────────────────────────────────────────

        private void EnsureResources()
        {
            EnsureBaseResources();
            if (_edgeVao != -1) return;
            MakeStaticVao(ref _edgeVao, ref _edgeVbo, Edges);
            MakeStaticVao(ref _faceVao, ref _faceVbo, Faces);
        }

        public override void Dispose()
        {
            if (_edgeVao != -1) { GL.DeleteVertexArray(_edgeVao); GL.DeleteBuffer(_edgeVbo); _edgeVao = -1; }
            if (_faceVao != -1) { GL.DeleteVertexArray(_faceVao); GL.DeleteBuffer(_faceVbo); _faceVao = -1; }
            base.Dispose();
        }
    }
}
