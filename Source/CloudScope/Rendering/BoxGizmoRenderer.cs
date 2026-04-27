using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    /// <summary>
    /// Renders the BoxSelectionTool gizmo for all three phases.
    ///   Phase Drawing:   2D screen rectangle overlay
    ///   Phase Extruding: 3D wireframe with highlighted depth edges + extrude hint
    ///   Phase Editing:   wireframe + face/corner/center handles + 3 rotation rings
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

        // Unit cube edges: 0-7 = front face (Z=-1), 8-15 = back face (Z=+1), 16-23 = depth edges
        private static readonly float[] UnitCubeEdges =
        {
            -1,-1,-1,  1,-1,-1,   1,-1,-1,  1, 1,-1,
             1, 1,-1, -1, 1,-1,  -1, 1,-1, -1,-1,-1,  // front face
            -1,-1, 1,  1,-1, 1,   1,-1, 1,  1, 1, 1,
             1, 1, 1, -1, 1, 1,  -1, 1, 1, -1,-1, 1,  // back face
            -1,-1,-1, -1,-1, 1,   1,-1,-1,  1,-1, 1,
             1, 1,-1,  1, 1, 1,  -1, 1,-1, -1, 1, 1,  // depth edges
        };

        // ── Public entry points ──────────────────────────────────────────────

        public void Render(BoxSelectionTool box, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            EnsureResources();

            if (box.Phase == ToolPhase.Extruding)
                RenderExtruding(box, view, proj, cam);
            else if (box.Phase == ToolPhase.Editing)
                RenderEditing(box, view, proj, cam);
        }

        public void RenderPlacementRect(int startX, int startY, int endX, int endY,
                                        int vpW, int vpH)
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

            // Filled interior
            float[] fill = { x0,y0,0, x1,y0,0, x1,y1,0, x0,y0,0, x1,y1,0, x0,y1,0 };
            UploadDynamic(fill);
            SetColor(0.0f, 0.78f, 1.0f, 0.12f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            // Border
            float[] border = {
                x0,y0,0, x1,y0,0,  x1,y0,0, x1,y1,0,
                x1,y1,0, x0,y1,0,  x0,y1,0, x0,y0,0
            };
            UploadDynamic(border);
            SetColor(0.0f, 0.82f, 1.0f, 0.85f);
            GL.LineWidth(1.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 8);

            // Corner accents
            float d = Math.Max(Math.Min(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 0.18f, 0.008f);
            float[] corners = {
                x0,y0,0, x0+d,y0,0,  x0,y0,0, x0,y0+d,0,
                x1,y0,0, x1-d,y0,0,  x1,y0,0, x1,y0+d,0,
                x1,y1,0, x1-d,y1,0,  x1,y1,0, x1,y1-d,0,
                x0,y1,0, x0+d,y1,0,  x0,y1,0, x0,y1-d,0,
            };
            UploadDynamic(corners);
            SetColor(1.0f, 1.0f, 1.0f, 0.95f);
            GL.LineWidth(2.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 16);

            GL.Enable(EnableCap.DepthTest);
        }

        // ── Phase 2: Extruding ───────────────────────────────────────────────

        private void RenderExtruding(BoxSelectionTool box, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            Matrix4 model = box.GetModelMatrix();
            Matrix4 mvp   = model * view * proj;

            GL.UseProgram(_shader);
            GL.BindVertexArray(_vao);
            GL.UniformMatrix4(_uMVP, false, ref mvp);

            // Front/back faces: subtle cyan
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            SetColor(0.0f, 0.75f, 1.0f, 0.55f);
            GL.LineWidth(1.2f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 16);  // front + back face edges

            // Depth edges (Z-direction): bright orange → shows extrude direction
            SetColor(1.0f, 0.55f, 0.0f, 0.9f);
            GL.LineWidth(2.0f);
            GL.DrawArrays(PrimitiveType.Lines, 16, 8);  // depth edges only

            // Ghost pass
            GL.Disable(EnableCap.DepthTest);
            SetColor(0.0f, 0.75f, 1.0f, 0.15f);
            GL.LineWidth(1.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 16);
            SetColor(1.0f, 0.55f, 0.0f, 0.25f);
            GL.DrawArrays(PrimitiveType.Lines, 16, 8);

            // Extrude hint: small arrow along local +Z at center
            RenderExtrudeArrow(box, cam);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        private void RenderExtrudeArrow(BoxSelectionTool box, OrbitCamera cam)
        {
            // Draw a simple line from front face to center, indicating extrude axis
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            Vector3 worldZ = invRot * Vector3.UnitZ;
            Vector3 tipWorld = box.Center + worldZ * (box.HalfExtents.Z * 1.6f);

            var (sx0, sy0, b0) = cam.WorldToScreen(box.ScreenFaceCenter);
            var (sx1, sy1, b1) = cam.WorldToScreen(tipWorld);
            if (b0 || b1) return;

            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;
            float nx0 = sx0 / vpW * 2f - 1f, ny0 = 1f - sy0 / vpH * 2f;
            float nx1 = sx1 / vpW * 2f - 1f, ny1 = 1f - sy1 / vpH * 2f;

            float[] arrow = { nx0, ny0, 0, nx1, ny1, 0 };
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uMVP, false, ref ident);
            GL.Disable(EnableCap.DepthTest);
            UploadDynamic(arrow);
            SetColor(1.0f, 0.75f, 0.0f, 0.9f);
            GL.LineWidth(2.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }

        // ── Phase 3: Editing ─────────────────────────────────────────────────

        private void RenderEditing(BoxSelectionTool box, Matrix4 view, Matrix4 proj, OrbitCamera cam)
        {
            Matrix4 model = box.GetModelMatrix();
            Matrix4 mvp   = model * view * proj;

            GL.UseProgram(_shader);
            GL.BindVertexArray(_vao);

            Vector4 wireColor = box.CurrentAction switch
            {
                EditAction.Grab   => new(0.2f, 1.0f, 0.3f, 0.8f),
                EditAction.Scale  => new(1.0f, 0.6f, 0.1f, 0.8f),
                EditAction.Rotate => new(1.0f, 1.0f, 0.2f, 0.8f),
                _                 => new(0.0f, 0.8f, 1.0f, 0.7f),
            };

            // Depth-tested wireframe
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.UniformMatrix4(_uMVP, false, ref mvp);
            SetColor(wireColor);
            GL.LineWidth(1.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);

            // Ghost wireframe
            GL.Disable(EnableCap.DepthTest);
            SetColor(wireColor.X, wireColor.Y, wireColor.Z, wireColor.W * 0.18f);
            GL.LineWidth(1.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 24);

            // Handles and rings
            RenderHandles(box, cam, wireColor);
            RenderRings(box, cam);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        private void RenderHandles(BoxSelectionTool box, OrbitCamera cam, Vector4 wireColor)
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

                float size = BoxSelectionTool.IsCenterHandle(i) ? 6f
                           : BoxSelectionTool.IsFaceHandle(i)   ? 5f : 4f;
                float hx = size / vpW, hy = size / vpH;

                Vector4 col;
                if (i == box.HoveredHandle)
                    col = new(1f, 1f, 0.2f, 0.95f);         // hover: yellow
                else if (box.IsHandleDragging)
                    col = wireColor;
                else if (BoxSelectionTool.IsCenterHandle(i))
                    col = new(0.3f, 1f, 0.4f, 0.80f);        // center: green
                else if (BoxSelectionTool.IsFaceHandle(i))
                    col = new(1f, 1f, 1f, 0.75f);             // face: white
                else
                    col = new(0f, 0.85f, 1f, 0.70f);          // corner: cyan

                float[] diamond = {
                    nx,    ny+hy, 0,  nx+hx, ny,    0,  nx,    ny-hy, 0,
                    nx,    ny+hy, 0,  nx,    ny-hy, 0,  nx-hx, ny,    0,
                };
                UploadDynamic(diamond);
                SetColor(col);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            }
        }

        private void RenderRings(BoxSelectionTool box, OrbitCamera cam)
        {
            const int N = 48;
            float radius = box.RingRadius;

            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            float vpW = cam.ViewportWidth, vpH = cam.ViewportHeight;

            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uMVP, false, ref ident);
            GL.Disable(EnableCap.DepthTest);

            // Ring colors: X=red, Y=green, Z=blue (standard 3D gizmo convention)
            Vector4[] baseColors =
            {
                new(1.0f, 0.25f, 0.25f, 0.75f),
                new(0.25f, 1.0f, 0.35f, 0.75f),
                new(0.25f, 0.55f, 1.0f, 0.75f),
            };

            var ringVerts = new float[N * 2 * 3]; // N segments × 2 verts × 3 floats

            for (int axis = 0; axis < 3; axis++)
            {
                bool hovered = (box.HoveredHandle == 15 + axis);
                Vector4 col  = hovered ? new(1f, 1f, 0.2f, 0.95f) : baseColors[axis];
                float lw     = hovered ? 2.5f : 1.8f;

                int vc = 0;
                float prevNx = 0, prevNy = 0;
                bool  prevOk = false;

                for (int j = 0; j <= N; j++)
                {
                    float theta = j * MathF.Tau / N;
                    float ct = MathF.Cos(theta), st = MathF.Sin(theta);

                    Vector3 local = axis switch
                    {
                        0 => new Vector3(0, ct, st),
                        1 => new Vector3(ct, 0, st),
                        _ => new Vector3(ct, st, 0),
                    } * radius;

                    Vector3 world = box.Center + invRot * local;
                    var (sx, sy, behind) = cam.WorldToScreen(world);

                    float nx = sx / vpW * 2f - 1f;
                    float ny = 1f - sy / vpH * 2f;

                    if (prevOk && !behind)
                    {
                        ringVerts[vc++] = prevNx; ringVerts[vc++] = prevNy; ringVerts[vc++] = 0;
                        ringVerts[vc++] = nx;     ringVerts[vc++] = ny;     ringVerts[vc++] = 0;
                    }

                    prevNx = nx; prevNy = ny; prevOk = !behind;
                }

                if (vc == 0) continue;

                UploadDynamic(ringVerts, vc);
                SetColor(col);
                GL.LineWidth(lw);
                GL.DrawArrays(PrimitiveType.Lines, 0, vc / 3);
            }
        }

        // ── GL helpers ───────────────────────────────────────────────────────

        private void UploadDynamic(float[] data, int count = -1)
        {
            int bytes = (count < 0 ? data.Length : count) * sizeof(float);
            GL.BindVertexArray(_hVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _hVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, bytes, data, BufferUsageHint.DynamicDraw);
        }

        private void SetColor(float r, float g, float b, float a) =>
            GL.Uniform4(_uColor, r, g, b, a);

        private void SetColor(Vector4 c) =>
            GL.Uniform4(_uColor, c.X, c.Y, c.Z, c.W);

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
            GL.BufferData(BufferTarget.ArrayBuffer,
                UnitCubeEdges.Length * sizeof(float), UnitCubeEdges, BufferUsageHint.StaticDraw);
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
