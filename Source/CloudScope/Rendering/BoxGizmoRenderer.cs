using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Renders the BoxSelectionTool gizmo.
    ///   Drawing phase:  2D rubber-band overlay
    ///   Editing phase:  3D wireframe + handles + rotation rings
    ///                   When box is flat: handle 5 (+Z) shows a large extrude arrow
    /// </summary>
    public sealed class BoxGizmoRenderer : IDisposable
    {
        private int _vao = -1, _vbo = -1;
        private int _hVao = -1, _hVbo = -1;
        private int _shader = -1;
        private int _uMVP, _uColor;

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

        // Unit-cube edges: 0-7 = front (Z=-1), 8-15 = back (Z=+1), 16-23 = depth edges
        private static readonly float[] UnitCubeEdges =
        {
            -1,-1,-1,  1,-1,-1,   1,-1,-1,  1, 1,-1,
             1, 1,-1, -1, 1,-1,  -1, 1,-1, -1,-1,-1,
            -1,-1, 1,  1,-1, 1,   1,-1, 1,  1, 1, 1,
             1, 1, 1, -1, 1, 1,  -1, 1, 1, -1,-1, 1,
            -1,-1,-1, -1,-1, 1,   1,-1,-1,  1,-1, 1,
             1, 1,-1,  1, 1, 1,  -1, 1,-1, -1, 1, 1,
        };

        // ── Entry points ─────────────────────────────────────────────────────

        public void Render(BoxSelectionTool box, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            EnsureResources();
            RenderEditing(box, view, proj, cam);
        }

        public void RenderPlacementRect(int startX, int startY, int endX, int endY, int vpW, int vpH)
        {
            EnsureResources();

            float x0 = Math.Min(startX, endX) / (float)vpW * 2f - 1f;
            float x1 = Math.Max(startX, endX) / (float)vpW * 2f - 1f;
            float y0 = 1f - Math.Max(startY, endY) / (float)vpH * 2f;
            float y1 = 1f - Math.Min(startY, endY) / (float)vpH * 2f;
            if (Math.Abs(x1 - x0) < 0.002f && Math.Abs(y1 - y0) < 0.002f) return;

            Matrix4 ident = Matrix4.Identity;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uMVP, false, ref ident);
            GL.Disable(EnableCap.DepthTest);

            // Fill
            Upload(new float[] { x0,y0,0, x1,y0,0, x1,y1,0, x0,y0,0, x1,y1,0, x0,y1,0 });
            SetColor(0f, 0.78f, 1f, 0.12f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            // Border
            Upload(new float[] { x0,y0,0, x1,y0,0, x1,y0,0, x1,y1,0, x1,y1,0, x0,y1,0, x0,y1,0, x0,y0,0 });
            SetColor(0f, 0.82f, 1f, 0.85f);
            GL.LineWidth(1.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 8);

            // Corner accents
            float d = Math.Max(Math.Min(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 0.18f, 0.008f);
            Upload(new float[] {
                x0,y0,0, x0+d,y0,0,  x0,y0,0, x0,y0+d,0,
                x1,y0,0, x1-d,y0,0,  x1,y0,0, x1,y0+d,0,
                x1,y1,0, x1-d,y1,0,  x1,y1,0, x1,y1-d,0,
                x0,y1,0, x0+d,y1,0,  x0,y1,0, x0,y1-d,0,
            });
            SetColor(1f, 1f, 1f, 0.95f);
            GL.LineWidth(2f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 16);

            GL.Enable(EnableCap.DepthTest);
        }

        // ── Editing phase ─────────────────────────────────────────────────────

        private void RenderEditing(BoxSelectionTool box, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            Matrix4 model = box.GetModelMatrix();
            Matrix4 mvp   = model * view * proj;

            GL.UseProgram(_shader);
            GL.BindVertexArray(_vao);

            Vector4 wire = box.CurrentAction switch
            {
                EditAction.Grab   => new(0.2f, 1.0f, 0.3f, 0.8f),
                EditAction.Scale  => new(1.0f, 0.6f, 0.1f, 0.8f),
                EditAction.Rotate => new(1.0f, 1.0f, 0.2f, 0.8f),
                _                 => new(0.0f, 0.8f, 1.0f, 0.7f),
            };

            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            SetColor(wire);
            GL.LineWidth(1.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);

            GL.Disable(EnableCap.DepthTest);
            SetColor(wire.X, wire.Y, wire.Z, wire.W * 0.18f);
            GL.LineWidth(1f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);

            RenderHandles(box, cam, wire);
            RenderRings(box, cam);

            if (box.IsFlat)
                RenderExtrudeArrow(box, cam);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // ── Handles ──────────────────────────────────────────────────────────

        private void RenderHandles(BoxSelectionTool box, OrbitCamera cam, Vector4 wire)
        {
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uMVP, false, ref ident);
            GL.Disable(EnableCap.DepthTest);
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;

            for (int i = 0; i < 15; i++)
            {
                var (sx, sy, behind) = cam.WorldToScreen(box.HandleWorldPosition(i));
                if (behind) continue;

                float nx = sx / vpW * 2f - 1f;
                float ny = 1f - sy / vpH * 2f;
                bool  isExtrude = i == BoxSelectionTool.ExtrudeHandle && box.IsFlat;
                float size = isExtrude ? 7f : BoxSelectionTool.IsCenterHandle(i) ? 6f : BoxSelectionTool.IsFaceHandle(i) ? 5f : 4f;
                float hx = size / vpW, hy = size / vpH;

                Vector4 col;
                if (i == box.HoveredHandle)
                    col = new(1f, 1f, 0.15f, 1f);
                else if (isExtrude)
                    col = new(1f, 0.55f, 0f, 0.95f);           // orange = extrude
                else if (BoxSelectionTool.IsCenterHandle(i))
                    col = new(0.3f, 1f, 0.4f, 0.8f);
                else if (BoxSelectionTool.IsFaceHandle(i))
                    col = new(1f, 1f, 1f, 0.75f);
                else
                    col = new(0f, 0.85f, 1f, 0.7f);

                Upload(new float[] {
                    nx, ny+hy, 0,  nx+hx, ny, 0,  nx, ny-hy, 0,
                    nx, ny+hy, 0,  nx, ny-hy, 0,  nx-hx, ny, 0,
                });
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }
        }

        // ── Extrude arrow ─────────────────────────────────────────────────────

        private void RenderExtrudeArrow(BoxSelectionTool box, OrbitCamera cam)
        {
            // Draw a screen-space arrow from the +Z face center outward
            Matrix3 invRot  = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            Vector3 worldZ  = invRot * Vector3.UnitZ;
            Vector3 facePos = box.HandleWorldPosition(BoxSelectionTool.ExtrudeHandle);
            Vector3 tipPos  = facePos + worldZ * MathF.Max(box.HalfExtents.X, box.HalfExtents.Y) * 0.4f;

            var (fx, fy, fb) = cam.WorldToScreen(facePos);
            var (tx, ty, tb) = cam.WorldToScreen(tipPos);
            if (fb || tb) return;

            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            float fnx = fx / vpW * 2f - 1f, fny = 1f - fy / vpH * 2f;
            float tnx = tx / vpW * 2f - 1f, tny = 1f - ty / vpH * 2f;

            // Arrow shaft
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uMVP, false, ref ident);
            GL.Disable(EnableCap.DepthTest);
            Upload(new float[] { fnx, fny, 0, tnx, tny, 0 });
            SetColor(1f, 0.6f, 0f, 0.9f);
            GL.LineWidth(2.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);

            // Arrowhead
            float dx = tnx - fnx, dy = tny - fny;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float nx2 = dx / len, ny2 = dy / len;
            float px = -ny2, py = nx2;  // perpendicular
            float hs = 0.015f;
            Upload(new float[] {
                tnx, tny, 0,
                tnx - nx2 * hs * 2f + px * hs, tny - ny2 * hs * 2f + py * hs, 0,
                tnx - nx2 * hs * 2f - px * hs, tny - ny2 * hs * 2f - py * hs, 0,
            });
            SetColor(1f, 0.6f, 0f, 0.95f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        // ── Rotation rings ───────────────────────────────────────────────────

        private void RenderRings(BoxSelectionTool box, OrbitCamera cam)
        {
            const int N      = 48;
            float     radius = box.RingRadius;
            Matrix3   invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;

            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uMVP, false, ref ident);
            GL.Disable(EnableCap.DepthTest);

            Vector4[] baseCol = {
                new(1.0f, 0.25f, 0.25f, 0.75f),
                new(0.25f, 1.0f, 0.35f, 0.75f),
                new(0.25f, 0.55f, 1.0f, 0.75f),
            };

            var verts = new float[N * 6]; // N segments × 2 verts × 3 floats

            for (int axis = 0; axis < 3; axis++)
            {
                bool    hov = box.HoveredHandle == 15 + axis;
                Vector4 col = hov ? new(1f, 1f, 0.15f, 1f) : baseCol[axis];
                GL.LineWidth(hov ? 2.5f : 1.8f);

                int    vc = 0;
                float  psx = 0, psy = 0;
                bool   pok = false;

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
                        verts[vc++] = psx; verts[vc++] = psy; verts[vc++] = 0;
                        verts[vc++] = nx;  verts[vc++] = ny;  verts[vc++] = 0;
                    }
                    psx = nx; psy = ny; pok = !behind;
                }

                if (vc == 0) continue;
                Upload(verts, vc);
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Lines, 0, vc / 3);
            }
        }

        // ── GL helpers ───────────────────────────────────────────────────────

        private void Upload(float[] data, int count = -1)
        {
            int bytes = (count < 0 ? data.Length : count) * sizeof(float);
            GL.BindVertexArray(_hVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _hVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, bytes, data, BufferUsageHint.DynamicDraw);
        }

        private void SetColor(float r, float g, float b, float a) => GL.Uniform4(_uColor, r, g, b, a);
        private void SetColor(Vector4 c) => GL.Uniform4(_uColor, c.X, c.Y, c.Z, c.W);

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

            _vao = GL.GenVertexArray(); _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, UnitCubeEdges.Length * sizeof(float),
                          UnitCubeEdges, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);

            _hVao = GL.GenVertexArray(); _hVbo = GL.GenBuffer();
            GL.BindVertexArray(_hVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _hVbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);
        }

        public void Dispose()
        {
            if (_shader != -1) GL.DeleteProgram(_shader);
            if (_vao    != -1) GL.DeleteVertexArray(_vao);
            if (_vbo    != -1) GL.DeleteBuffer(_vbo);
            if (_hVao   != -1) GL.DeleteVertexArray(_hVao);
            if (_hVbo   != -1) GL.DeleteBuffer(_hVbo);
        }
    }
}
