using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Professional OBB gizmo renderer.
    ///
    /// Drawing phase  : 2D rubber-band overlay
    /// Editing phase  : layered rendering
    ///   1. Semi-transparent face fills  (axis-tinted, shows volume + orientation)
    ///   2. Local axis lines X/Y/Z       (orientation reference)
    ///   3. Wireframe                    (cyan, depth-tested + ghost)
    ///   4. Face arrow handles           (axis-colored, ~30 px arrows)
    ///   5. Corner handles               (white diamonds, 9 px)
    ///   6. Rotation rings               (R/G/B, arc segments)
    ///   7. Extrude arrow                (orange, only when box is flat)
    /// </summary>
    public sealed class BoxGizmoRenderer : IDisposable
    {
        // ── GL resources ─────────────────────────────────────────────────────
        private int _edgeVao = -1, _edgeVbo = -1;   // static: unit-cube edges
        private int _faceVao = -1, _faceVbo = -1;   // static: unit-cube faces (filled)
        private int _axisVao = -1, _axisVbo = -1;   // static: 3 axis lines
        private int _dynVao  = -1, _dynVbo  = -1;   // dynamic: handles, rings, arrows
        private int _shader  = -1;
        private int _uMVP, _uColor;

        private const string Vert = @"
#version 330 core
layout(location=0) in vec3 aPos;
uniform mat4 uMVP;
void main() { gl_Position = uMVP * vec4(aPos,1.0); }";

        private const string Frag = @"
#version 330 core
uniform vec4 uColor;
out vec4 FragColor;
void main() { FragColor = uColor; }";

        // Unit-cube edges (24 verts = 12 edges)
        private static readonly float[] Edges =
        {
            -1,-1,-1,  1,-1,-1,   1,-1,-1,  1, 1,-1,
             1, 1,-1, -1, 1,-1,  -1, 1,-1, -1,-1,-1,
            -1,-1, 1,  1,-1, 1,   1,-1, 1,  1, 1, 1,
             1, 1, 1, -1, 1, 1,  -1, 1, 1, -1,-1, 1,
            -1,-1,-1, -1,-1, 1,   1,-1,-1,  1,-1, 1,
             1, 1,-1,  1, 1, 1,  -1, 1,-1, -1, 1, 1,
        };

        // Unit-cube faces: 6 faces × 2 triangles × 3 verts = 36 verts
        // Order: -X, +X, -Y, +Y, -Z, +Z  (2 triangles per face, 6 verts)
        private static readonly float[] Faces =
        {
            -1,-1,-1, -1, 1,-1, -1, 1, 1,   -1,-1,-1, -1, 1, 1, -1,-1, 1, // -X
             1,-1,-1,  1, 1,-1,  1, 1, 1,    1,-1,-1,  1, 1, 1,  1,-1, 1, // +X
            -1,-1,-1,  1,-1,-1,  1,-1, 1,   -1,-1,-1,  1,-1, 1, -1,-1, 1, // -Y
            -1, 1,-1,  1, 1,-1,  1, 1, 1,   -1, 1,-1,  1, 1, 1, -1, 1, 1, // +Y
            -1,-1,-1,  1,-1,-1,  1, 1,-1,   -1,-1,-1,  1, 1,-1, -1, 1,-1, // -Z
            -1,-1, 1,  1,-1, 1,  1, 1, 1,   -1,-1, 1,  1, 1, 1, -1, 1, 1, // +Z
        };

        // Local axis lines: -axis → +axis (rendered with model matrix → world face centers)
        private static readonly float[] AxisLines =
        {
            -1, 0, 0,   1, 0, 0,   // X
             0,-1, 0,   0, 1, 0,   // Y
             0, 0,-1,   0, 0, 1,   // Z
        };

        // Per-axis colors: index=0→X, 1→Y, 2→Z
        private static readonly Vector4[] AxisColor =
        {
            new(0.95f, 0.20f, 0.20f, 1.00f),  // X red
            new(0.20f, 0.95f, 0.30f, 1.00f),  // Y green
            new(0.25f, 0.55f, 1.00f, 1.00f),  // Z blue
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

        // ── Public entry points ──────────────────────────────────────────────

        public void Render(BoxSelectionTool box, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            EnsureResources();
            Matrix4 model = box.GetModelMatrix();
            Matrix4 mvp   = model * view * proj;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            RenderFaceFills(mvp);
            RenderAxes(mvp, box);
            RenderWireframe(mvp);
            RenderFaceArrows(box, cam);
            RenderCornerHandles(box, cam);
            RenderRings(box, cam);
            if (box.IsFlat) RenderExtrudeArrow(box, cam);

            GL.Disable(EnableCap.Blend);
        }

        public void RenderPlacementRect(int x0, int y0, int x1, int y1, int vpW, int vpH)
        {
            EnsureResources();
            float nx0 = Math.Min(x0,x1) / (float)vpW * 2f - 1f;
            float nx1 = Math.Max(x0,x1) / (float)vpW * 2f - 1f;
            float ny0 = 1f - Math.Max(y0,y1) / (float)vpH * 2f;
            float ny1 = 1f - Math.Min(y0,y1) / (float)vpH * 2f;
            if (Math.Abs(nx1-nx0) < 0.002f && Math.Abs(ny1-ny0) < 0.002f) return;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.UseProgram(_shader);
            Matrix4 id = Matrix4.Identity;
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);

            SetColor(0f, 0.78f, 1f, 0.12f);
            Dyn(new[]{ nx0,ny0,0f, nx1,ny0,0f, nx1,ny1,0f, nx0,ny0,0f, nx1,ny1,0f, nx0,ny1,0f });
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            SetColor(0f, 0.82f, 1f, 0.85f); GL.LineWidth(1.5f);
            Dyn(new[]{ nx0,ny0,0f, nx1,ny0,0f, nx1,ny0,0f, nx1,ny1,0f, nx1,ny1,0f, nx0,ny1,0f, nx0,ny1,0f, nx0,ny0,0f });
            GL.DrawArrays(PrimitiveType.Lines, 0, 8);

            float d = Math.Max(Math.Min(Math.Abs(nx1-nx0), Math.Abs(ny1-ny0)) * 0.18f, 0.008f);
            SetColor(1f,1f,1f,0.95f); GL.LineWidth(2f);
            Dyn(new[]{
                nx0,ny0,0f, nx0+d,ny0,0f,  nx0,ny0,0f, nx0,ny0+d,0f,
                nx1,ny0,0f, nx1-d,ny0,0f,  nx1,ny0,0f, nx1,ny0+d,0f,
                nx1,ny1,0f, nx1-d,ny1,0f,  nx1,ny1,0f, nx1,ny1-d,0f,
                nx0,ny1,0f, nx0+d,ny1,0f,  nx0,ny1,0f, nx0,ny1-d,0f,
            });
            GL.DrawArrays(PrimitiveType.Lines, 0, 16);

            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }

        // ── Layer 1: Face fills ──────────────────────────────────────────────

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

        // ── Layer 2: Local axis lines ────────────────────────────────────────

        private void RenderAxes(Matrix4 mvp, BoxSelectionTool box)
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

        // ── Layer 3: Wireframe ───────────────────────────────────────────────

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

        // ── Layer 4: Face arrow handles ──────────────────────────────────────

        private void RenderFaceArrows(BoxSelectionTool box, OrbitCamera cam)
        {
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            float arrowWorld = MathF.Max(MathF.Max(box.HalfExtents.X, box.HalfExtents.Y), box.HalfExtents.Z) * 0.22f;
            arrowWorld = MathF.Max(arrowWorld, 0.01f);

            Matrix4 id = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            for (int i = 0; i < 6; i++)
            {
                Vector3 faceWorld = box.HandleWorldPosition(i);
                // Outward direction in world space
                Vector3 localDir  = BoxSelectionTool.HandleLocalPos[i]; // e.g. (1,0,0)
                Vector3 worldDir  = invRot * localDir;
                Vector3 tipWorld  = faceWorld + worldDir * arrowWorld;

                var (fx, fy, fb) = cam.WorldToScreen(faceWorld);
                var (tx, ty, tb) = cam.WorldToScreen(tipWorld);
                if (fb || tb) continue;

                float fnx = fx / vpW * 2f - 1f, fny = 1f - fy / vpH * 2f;
                float tnx = tx / vpW * 2f - 1f, tny = 1f - ty / vpH * 2f;

                // Color: axis-based, orange for extrude handle when flat, yellow when hovered
                int   ax  = i / 2;
                Vector4 col;
                if (i == box.HoveredHandle)
                    col = new(1f, 1f, 0.15f, 1f);
                else if (i == BoxSelectionTool.ExtrudeHandle && box.IsFlat)
                    col = new(1f, 0.55f, 0f, 0.95f);
                else
                    col = AxisColor[ax] with { W = 0.90f };

                // Shaft: from face center outward
                Dyn(new[] { fnx, fny, 0f, tnx, tny, 0f });
                SetColor(col);
                GL.LineWidth(i == box.HoveredHandle ? 3f : 2f);
                GL.DrawArrays(PrimitiveType.Lines, 0, 2);

                // Circle "dot" at base (face center indicator)
                DrawScreenDot(fnx, fny, 4f / vpW, 4f / vpH, col);

                // Arrowhead triangle at tip
                float dx = tnx - fnx, dy = tny - fny;
                float len = MathF.Sqrt(dx*dx + dy*dy);
                if (len < 1e-4f) continue;
                float nx2 = dx/len, ny2 = dy/len, px = -ny2, py = nx2;
                float hs = 0.013f;
                Dyn(new[]{
                    tnx, tny, 0f,
                    tnx - nx2*hs*2f + px*hs, tny - ny2*hs*2f + py*hs, 0f,
                    tnx - nx2*hs*2f - px*hs, tny - ny2*hs*2f - py*hs, 0f,
                });
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 5: Corner handles ──────────────────────────────────────────

        private void RenderCornerHandles(BoxSelectionTool box, OrbitCamera cam)
        {
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            Matrix4 id = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            for (int i = 6; i <= 14; i++)   // corners 6-13 + center 14
            {
                var (sx, sy, behind) = cam.WorldToScreen(box.HandleWorldPosition(i));
                if (behind) continue;
                float nx = sx / vpW * 2f - 1f, ny = 1f - sy / vpH * 2f;

                float size = BoxSelectionTool.IsCenterHandle(i) ? 7f : 9f;
                float hx = size / vpW, hy = size / vpH;

                Vector4 col = i == box.HoveredHandle
                    ? new(1f, 1f, 0.15f, 1f)
                    : BoxSelectionTool.IsCenterHandle(i)
                        ? new(0.3f, 1f, 0.45f, 0.85f)   // center: green
                        : new(0.9f, 0.9f, 0.9f, 0.80f); // corner: white

                // Diamond (4 triangles)
                Dyn(new[]{
                    nx,    ny+hy, 0f,  nx+hx, ny,    0f,  nx,    ny-hy, 0f,
                    nx,    ny+hy, 0f,  nx,    ny-hy, 0f,  nx-hx, ny,    0f,
                });
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

                // Outline for better visibility
                Dyn(new[]{ nx, ny+hy, 0f, nx+hx, ny, 0f, nx, ny-hy, 0f, nx-hx, ny, 0f, nx, ny+hy, 0f });
                SetColor(col.X*0.4f, col.Y*0.4f, col.Z*0.4f, 0.7f);
                GL.LineWidth(1f);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, 5);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 6: Rotation rings ──────────────────────────────────────────

        private void RenderRings(BoxSelectionTool box, OrbitCamera cam)
        {
            const int N = 64;
            float radius = box.RingRadius;
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;

            Matrix4 id = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            var seg = new float[N * 6];

            for (int axis = 0; axis < 3; axis++)
            {
                bool    hov = box.HoveredHandle == 15 + axis;
                Vector4 col = hov ? new(1f, 1f, 0.15f, 1f)
                                  : AxisColor[axis] with { W = 0.80f };
                GL.LineWidth(hov ? 3f : 2f);

                int vc = 0;
                float psx = 0, psy = 0; bool pok = false;

                for (int j = 0; j <= N; j++)
                {
                    float t = j * MathF.Tau / N;
                    float ct = MathF.Cos(t), st = MathF.Sin(t);
                    Vector3 local = axis switch
                    {
                        0 => new Vector3(0, ct, st),
                        1 => new Vector3(ct, 0, st),
                        _ => new Vector3(ct, st, 0),
                    } * radius;

                    var (sx, sy, behind) = cam.WorldToScreen(box.Center + invRot * local);
                    float nx = sx / vpW * 2f - 1f, ny = 1f - sy / vpH * 2f;

                    if (pok && !behind)
                    {
                        seg[vc++]=psx; seg[vc++]=psy; seg[vc++]=0f;
                        seg[vc++]=nx;  seg[vc++]=ny;  seg[vc++]=0f;
                    }
                    psx=nx; psy=ny; pok=!behind;
                }

                if (vc == 0) continue;
                Dyn(seg, vc);
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Lines, 0, vc / 3);
            }

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Layer 7: Extrude arrow (flat box only) ───────────────────────────

        private void RenderExtrudeArrow(BoxSelectionTool box, OrbitCamera cam)
        {
            Matrix3 invRot  = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            Vector3 worldZ  = invRot * Vector3.UnitZ;
            Vector3 facePos = box.HandleWorldPosition(BoxSelectionTool.ExtrudeHandle);
            float   extLen  = MathF.Max(box.HalfExtents.X, box.HalfExtents.Y) * 0.55f;
            Vector3 tipPos  = facePos + worldZ * extLen;

            var (fx, fy, fb) = cam.WorldToScreen(facePos);
            var (tx, ty, tb) = cam.WorldToScreen(tipPos);
            if (fb || tb) return;

            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            float fnx = fx/vpW*2f-1f, fny = 1f-fy/vpH*2f;
            float tnx = tx/vpW*2f-1f, tny = 1f-ty/vpH*2f;

            Matrix4 id = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref id);
            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);

            SetColor(1f, 0.6f, 0f, 0.95f);
            Dyn(new[]{ fnx,fny,0f, tnx,tny,0f });
            GL.LineWidth(3f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);

            float dx = tnx-fnx, dy = tny-fny;
            float len = MathF.Sqrt(dx*dx + dy*dy);
            if (len < 1e-4f) return;
            float nx2 = dx/len, ny2 = dy/len, px = -ny2, py = nx2, hs = 0.02f;
            Dyn(new[]{
                tnx, tny, 0f,
                tnx-nx2*hs*2f+px*hs, tny-ny2*hs*2f+py*hs, 0f,
                tnx-nx2*hs*2f-px*hs, tny-ny2*hs*2f-py*hs, 0f,
            });
            SetColor(1f, 0.6f, 0f, 1f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void DrawScreenDot(float nx, float ny, float hx, float hy, Vector4 col)
        {
            Dyn(new[]{
                nx,    ny+hy, 0f,  nx+hx, ny,    0f,  nx,    ny-hy, 0f,
                nx,    ny+hy, 0f,  nx,    ny-hy, 0f,  nx-hx, ny,    0f,
            });
            SetColor(col);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        private void Dyn(float[] data, int count = -1)
        {
            int bytes = (count < 0 ? data.Length : count) * sizeof(float);
            GL.BindVertexArray(_dynVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _dynVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, bytes, data, BufferUsageHint.DynamicDraw);
        }

        private void SetColor(float r, float g, float b, float a) => GL.Uniform4(_uColor, r, g, b, a);
        private void SetColor(Vector4 c) => GL.Uniform4(_uColor, c.X, c.Y, c.Z, c.W);

        // ── Resource init ────────────────────────────────────────────────────

        private void EnsureResources()
        {
            if (_shader != -1) return;

            // Shader
            int v = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(v, Vert); GL.CompileShader(v);
            int f = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(f, Frag); GL.CompileShader(f);
            _shader = GL.CreateProgram();
            GL.AttachShader(_shader, v); GL.AttachShader(_shader, f);
            GL.LinkProgram(_shader);
            GL.DeleteShader(v); GL.DeleteShader(f);
            _uMVP   = GL.GetUniformLocation(_shader, "uMVP");
            _uColor = GL.GetUniformLocation(_shader, "uColor");

            MakeStaticVao(ref _edgeVao, ref _edgeVbo, Edges);
            MakeStaticVao(ref _faceVao, ref _faceVbo, Faces);
            MakeStaticVao(ref _axisVao, ref _axisVbo, AxisLines);

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
            if (_edgeVao != -1) { GL.DeleteVertexArray(_edgeVao); GL.DeleteBuffer(_edgeVbo); }
            if (_faceVao != -1) { GL.DeleteVertexArray(_faceVao); GL.DeleteBuffer(_faceVbo); }
            if (_axisVao != -1) { GL.DeleteVertexArray(_axisVao); GL.DeleteBuffer(_axisVbo); }
            if (_dynVao  != -1) { GL.DeleteVertexArray(_dynVao);  GL.DeleteBuffer(_dynVbo);  }
        }
    }
}
