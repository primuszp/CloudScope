using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CloudScope
{
    /// <summary>
    /// Modern OpenGL 3.3 point-cloud viewer.
    ///
    /// Camera controls (AdvancedZPR mapping):
    ///   Left drag     Orbit (azimuth / elevation)
    ///   Right drag    Pan  â€” the clicked point stays under the cursor
    ///   Scroll        Zoom â€” zooms toward the point under the cursor
    ///   Space         Toggle orthographic / perspective
    ///   Num1/3/7      Front / Right / Top
    ///   Num5          Isometric
    ///   R             Reset view
    ///   +/-           Point size up / down
    ///   Escape        Quit
    /// </summary>
    public sealed class PointCloudViewer : GameWindow
    {
        // ── GPU resources ─────────────────────────────────────────────────────
        private int _vao, _vbo;
        private int _shader, _lineShader, _sphereShader;
        private int _pointCount;

        // ── Uniform locations (cached) ────────────────────────────────────────
        private int _uView, _uProj, _uPointSize;
        private int _uViewLine, _uProjLine;
        private int _uViewSphere, _uProjSphere, _uPointSizeSphere;

        // â”€â”€ Camera â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly OrbitCamera _cam = new();
        private float _cloudRadius = 50f;

        // â”€â”€ Mouse state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private int  _lastMX, _lastMY;
        private bool _leftDown, _rightDown;

        // â”€â”€ Point rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private float _pointSize = 1.5f;

        // â”€â”€ Vertex shader â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // No distance attenuation: gl_PointSize = pointSize (constant screen pixels).
        // This keeps points crisp and small regardless of zoom.
        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aCol;

out vec3 vColor;

uniform mat4 view;
uniform mat4 projection;
uniform float pointSize;

void main()
{
    gl_Position  = projection * view * vec4(aPos, 1.0);
    gl_PointSize = pointSize;
    vColor = aCol;
}
";

        // â”€â”€ Fragment shader for points â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Hard-clipped circle â€” no alpha blending, no soft edge.
        private const string FragSrc = @"
#version 330 core
in  vec3 vColor;
out vec4 FragColor;

void main()
{
    vec2  d = gl_PointCoord - vec2(0.5);
    if (dot(d, d) > 0.25) discard;
    FragColor = vec4(vColor, 1.0);
}
";

        // ── Fragment shader for lines (no gl_PointCoord discard) ──────────────
        private const string LineFragSrc = @"
#version 330 core
in  vec3 vColor;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vColor, 1.0);
}
";

        // ── Fragment shader for Sphere Impostor (Yellow Pivot) ────────────────
        private const string SphereFragSrc = @"
#version 330 core
out vec4 FragColor;

void main()
{
    vec2 p = gl_PointCoord * 2.0 - vec2(1.0);
    float r2 = dot(p, p);
    if (r2 > 1.0) discard;
    
    float z = sqrt(1.0 - r2);
    vec3 normal = vec3(p.x, -p.y, z);
    
    vec3 lightDir = normalize(vec3(1.0, 1.0, 1.0));
    float diff = max(dot(normal, lightDir), 0.3);
    
    vec3 color = vec3(1.0, 0.9, 0.1) * diff; // Yellow
    FragColor = vec4(color, 1.0);
}
";

        public PointCloudViewer(int width, int height)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height),
                Title      = "CloudScope - Point Cloud Viewer",
                APIVersion = new Version(3, 3),
                Profile    = ContextProfile.Core,
            }) { }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Initialisation
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        protected override void OnLoad()
        {
            base.OnLoad();
            GL.ClearColor(0.08f, 0.08f, 0.12f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ProgramPointSize);

            _shader  = BuildShader(VertSrc, FragSrc);
            _uView   = GL.GetUniformLocation(_shader, "view");
            _uProj   = GL.GetUniformLocation(_shader, "projection");
            _uPointSize = GL.GetUniformLocation(_shader, "pointSize");

            _lineShader = BuildShader(VertSrc, LineFragSrc);
            _uViewLine  = GL.GetUniformLocation(_lineShader, "view");
            _uProjLine  = GL.GetUniformLocation(_lineShader, "projection");

            _sphereShader = BuildShader(VertSrc, SphereFragSrc);
            _uViewSphere  = GL.GetUniformLocation(_sphereShader, "view");
            _uProjSphere  = GL.GetUniformLocation(_sphereShader, "projection");
            _uPointSizeSphere = GL.GetUniformLocation(_sphereShader, "pointSize");

            _cam.SetViewportSize(Size.X, Size.Y);
        }

        /// <summary>Upload point cloud to GPU. Must be called before Run().</summary>
        public void LoadPointCloud(PointData[] pts, float cloudRadius = 50f)
        {
            _pointCount  = pts.Length;
            _cloudRadius = cloudRadius;
            _cam.FitToCloud(cloudRadius);

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

            // Layout: X Y Z R G B  (6 floats = 24 bytes per vertex)
            var data = new float[pts.Length * 6];
            for (int i = 0; i < pts.Length; i++)
            {
                data[i * 6 + 0] = pts[i].X;
                data[i * 6 + 1] = pts[i].Y;
                data[i * 6 + 2] = pts[i].Z;
                data[i * 6 + 3] = pts[i].R;
                data[i * 6 + 4] = pts[i].G;
                data[i * 6 + 5] = pts[i].B;
            }
            GL.BufferData(BufferTarget.ArrayBuffer,
                data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, 12);
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(0);
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Keyboard (non-mouse controls)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            if (KeyboardState.IsKeyPressed(Keys.Escape)) Close();

            // Point size
            if (KeyboardState.IsKeyPressed(Keys.KeyPadAdd))
                _pointSize = MathF.Min(_pointSize + 0.5f, 20f);
            if (KeyboardState.IsKeyPressed(Keys.KeyPadSubtract))
                _pointSize = MathF.Max(_pointSize - 0.5f, 0.5f);

            // Standard views
            if (KeyboardState.IsKeyPressed(Keys.KeyPad1)) _cam.SetFrontView();
            if (KeyboardState.IsKeyPressed(Keys.KeyPad3)) _cam.SetRightView();
            if (KeyboardState.IsKeyPressed(Keys.KeyPad7)) _cam.SetTopView();
            if (KeyboardState.IsKeyPressed(Keys.KeyPad5)) _cam.SetIsometric();
            if (KeyboardState.IsKeyPressed(Keys.R))       _cam.ResetView(_cloudRadius);

            // WASD FPS Movement
            float dt = (float)args.Time;
            float moveSpeed = (float)_cam.Hvs * 2.0f * dt; // Travels screen width in 1 second

            float dx = 0, dy = 0, dz = 0;
            if (KeyboardState.IsKeyDown(Keys.W)) dz -= moveSpeed; // Forward
            if (KeyboardState.IsKeyDown(Keys.S)) dz += moveSpeed; // Backward
            if (KeyboardState.IsKeyDown(Keys.A)) dx -= moveSpeed; // Left
            if (KeyboardState.IsKeyDown(Keys.D)) dx += moveSpeed; // Right
            if (KeyboardState.IsKeyDown(Keys.E)) dy += moveSpeed; // Up
            if (KeyboardState.IsKeyDown(Keys.Q)) dy -= moveSpeed; // Down

            if (dx != 0 || dy != 0 || dz != 0)
            {
                _cam.MoveFPS(dx, dy, dz);
            }
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Mouse â€” mirrors AdvancedZPR event handler order exactly
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            
            int mx = (int)MouseState.Position.X;
            int my = (int)MouseState.Position.Y;

            if (e.Button == MouseButton.Left)
            {
                _leftDown = true;
                // For orbiting, pivot around the center crosshair with a window search
                _cam.PickDepthWindow(Size.X / 2, Size.Y / 2);
            }
            else if (e.Button == MouseButton.Right)
            {
                _rightDown = true;
                // For panning, pick exactly under the cursor so it stays under the mouse
                _cam.PickDepth(mx, my);
            }
            else
            {
                _cam.PickDepth(mx, my);
            }

            _lastMX = mx;
            _lastMY = my;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButton.Left)  _leftDown  = false;
            if (e.Button == MouseButton.Right) _rightDown = false;
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            int mx = (int)e.X;
            int my = (int)e.Y;

            if (_leftDown)
            {
                // Rotate: pixel delta
                int dx = mx - _lastMX;
                int dy = my - _lastMY;
                _cam.Rotate(dx, dy);
            }
            else if (_rightDown)
            {
                // Pan: from last pos -> to current pos
                _cam.Pan(_lastMX, _lastMY, mx, my);
            }

            _lastMX = mx;
            _lastMY = my;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            int mx = (int)MouseState.Position.X;
            int my = (int)MouseState.Position.Y;

            // PickDepth at cursor, then zoom towards cursor
            _cam.PickDepth(mx, my);

            float factor = e.OffsetY > 0 ? 1.25f : 1f / 1.25f;
            _cam.Zoom(mx, my, factor);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Keys.Space)
            {
                int cx = Size.X / 2;
                int cy = Size.Y / 2;
                _cam.PickDepth(cx, cy);
                _cam.ToggleProjection(cx, cy);
            }
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.UseProgram(_shader);
            var view = _cam.GetViewMatrix();
            var proj = _cam.GetProjectionMatrix();
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProj, false, ref proj);

            // 1. Draw point cloud
            if (_pointCount > 0)
            {
                GL.Uniform1(_uPointSize, _pointSize);
                GL.BindVertexArray(_vao);
                GL.DrawArrays(PrimitiveType.Points, 0, _pointCount);
            }

            // 2. Draw 3D pivot point indicator (only when user orbits)
            if (_leftDown)
            {
                RenderPivotIndicator(ref view, ref proj);
            }
            else
            {
                // 3. Draw 2D Center Crosshair (visible when not orbiting)
                RenderCenterCrosshair();
            }

            SwapBuffers();
        }

        private int _pivotVao = -1;
        private int _pivotVbo = -1;
        private int _pivotVertexCount = 0;

        private void RenderPivotIndicator(ref Matrix4 view, ref Matrix4 proj)
        {
            if (_pivotVao == -1)
            {
                // Generate trackball geometry (axes + circles)
                // We use GL_LINES.
                int segments = 64;
                // 3 axes * 2 verts = 6
                // 3 circles * segments * 2 verts = 3 * 128 = 384
                // Total = 390 vertices (2340 floats)
                _pivotVertexCount = 6 + 3 * segments * 2;
                float[] pivotData = new float[_pivotVertexCount * 6];
                int idx = 0;

                // Helper to add a vertex
                void AddV(float x, float y, float z, float r, float g, float b)
                {
                    pivotData[idx++] = x; pivotData[idx++] = y; pivotData[idx++] = z;
                    pivotData[idx++] = r; pivotData[idx++] = g; pivotData[idx++] = b;
                }

                // 1. Axes (Red X, Green Y, Blue Z)
                AddV(-1f, 0f, 0f, 1f, 0f, 0f); AddV(1f, 0f, 0f, 1f, 0f, 0f); // X
                AddV(0f, -1f, 0f, 0f, 1f, 0f); AddV(0f, 1f, 0f, 0f, 1f, 0f); // Y
                AddV(0f, 0f, -1f, 0f, 0f, 1f); AddV(0f, 0f, 1f, 0f, 0f, 1f); // Z

                // 2. Circles
                float step = MathF.PI * 2f / segments;
                for (int c = 0; c < 3; c++)
                {
                    float r = c == 0 ? 1f : 0f; // Red for X (YZ plane)
                    float g = c == 1 ? 1f : 0f; // Green for Y (XZ plane)
                    float b = c == 2 ? 1f : 0f; // Blue for Z (XY plane)

                    for (int i = 0; i < segments; i++)
                    {
                        float a1 = i * step;
                        float a2 = (i + 1) * step;
                        float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                        float c2 = MathF.Cos(a2), s2 = MathF.Sin(a2);

                        if (c == 0) // YZ plane
                        {
                            AddV(0, c1, s1, r, g, b); AddV(0, c2, s2, r, g, b);
                        }
                        else if (c == 1) // XZ plane
                        {
                            AddV(c1, 0, s1, r, g, b); AddV(c2, 0, s2, r, g, b);
                        }
                        else // XY plane
                        {
                            AddV(c1, s1, 0, r, g, b); AddV(c2, s2, 0, r, g, b);
                        }
                    }
                }

                _pivotVao = GL.GenVertexArray();
                _pivotVbo = GL.GenBuffer();
                GL.BindVertexArray(_pivotVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _pivotVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, pivotData.Length * sizeof(float), pivotData, BufferUsageHint.StaticDraw);
                
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, 12);
                GL.EnableVertexAttribArray(1);
            }

            Vector3 p = _cam.Pivot;
            
            // Draw Trackball Lines
            GL.UseProgram(_lineShader);
            
            // Trackball scale: dynamic to keep it roughly constant size on screen
            float scale = (float)_cam.Hvs * 0.3f;
            Matrix4 model = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(p);
            Matrix4 mv = model * view;
            
            GL.UniformMatrix4(_uViewLine, false, ref mv);
            GL.UniformMatrix4(_uProjLine, false, ref proj);

            GL.BindVertexArray(_pivotVao);
            
            GL.Disable(EnableCap.DepthTest);
            GL.LineWidth(2.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, _pivotVertexCount);
            GL.LineWidth(1.0f);
            
            // Draw Yellow Sphere at center
            GL.UseProgram(_sphereShader);
            GL.UniformMatrix4(_uViewSphere, false, ref view);
            GL.UniformMatrix4(_uProjSphere, false, ref proj);
            GL.Uniform1(_uPointSizeSphere, 20.0f); 

            // We can just draw the first vertex of the trackball but scaled to 0 (which is the center? No, first vertex is -1,0,0)
            // Let's quickly update a 1-point VBO for the sphere, or just draw the origin
            float[] sphereData = new float[] { p.X, p.Y, p.Z };
            // Actually, we can reuse _vbo or a separate one, but let's just make a small buffer for the sphere
            if (_sphereVao == -1)
            {
                _sphereVao = GL.GenVertexArray();
                _sphereVbo = GL.GenBuffer();
                GL.BindVertexArray(_sphereVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
                GL.EnableVertexAttribArray(0);
            }
            GL.BindVertexArray(_sphereVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 3 * sizeof(float), sphereData, BufferUsageHint.DynamicDraw);
            
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DrawArrays(PrimitiveType.Points, 0, 1);
            GL.Disable(EnableCap.Blend);
            
            GL.Enable(EnableCap.DepthTest);
        }

        private int _sphereVao = -1;
        private int _sphereVbo = -1;

        private int _crosshairVao = -1;
        private int _crosshairVbo = -1;

        private void RenderCenterCrosshair()
        {
            if (_crosshairVao == -1)
            {
                _crosshairVao = GL.GenVertexArray();
                _crosshairVbo = GL.GenBuffer();
                
                // 2D lines in NDC (-1 to 1)
                float size = 15f / Size.X; // 15 pixels half-size
                float aspect = (float)Size.X / Size.Y;
                float sizeY = size * aspect;
                
                // We'll update the VBO every frame anyway since window can resize.
                GL.BindVertexArray(_crosshairVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, 12);
                GL.EnableVertexAttribArray(1);
            }

            float sX = 15f / Size.X; // 15 pixels
            float sY = 15f / Size.Y;
            float g = 0.5f; // Gray color

            float[] crossData = new float[]
            {
                -sX, 0f, 0f,   g, g, g,
                 sX, 0f, 0f,   g, g, g,
                 0f, -sY, 0f,  g, g, g,
                 0f,  sY, 0f,  g, g, g
            };

            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, crossData.Length * sizeof(float), crossData, BufferUsageHint.DynamicDraw);

            GL.UseProgram(_lineShader);
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.UniformMatrix4(_uProjLine, false, ref ident);

            GL.Disable(EnableCap.DepthTest);
            // Draw a subtle dark shadow line first
            Matrix4 shadow = Matrix4.CreateTranslation(1f/Size.X, -1f/Size.Y, 0);
            GL.UniformMatrix4(_uViewLine, false, ref shadow);
            float[] shadowData = new float[]
            {
                -sX, 0f, 0f,   0f, 0f, 0f,
                 sX, 0f, 0f,   0f, 0f, 0f,
                 0f, -sY, 0f,  0f, 0f, 0f,
                 0f,  sY, 0f,  0f, 0f, 0f
            };
            GL.BufferData(BufferTarget.ArrayBuffer, shadowData.Length * sizeof(float), shadowData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);

            // Then draw gray lines
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.BufferData(BufferTarget.ArrayBuffer, crossData.Length * sizeof(float), crossData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);
            
            GL.Enable(EnableCap.DepthTest);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, e.Width, e.Height);
            _cam.SetViewportSize(e.Width, e.Height);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        protected override void OnUnload()
        {
            if (_pivotVao != -1) GL.DeleteVertexArray(_pivotVao);
            if (_pivotVbo != -1) GL.DeleteBuffer(_pivotVbo);
            
            if (_sphereVao != -1) GL.DeleteVertexArray(_sphereVao);
            if (_sphereVbo != -1) GL.DeleteBuffer(_sphereVbo);

            if (_crosshairVao != -1) GL.DeleteVertexArray(_crosshairVao);
            if (_crosshairVbo != -1) GL.DeleteBuffer(_crosshairVbo);
            
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            GL.DeleteProgram(_shader);
            GL.DeleteProgram(_lineShader);
            GL.DeleteProgram(_sphereShader);
            base.OnUnload();
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Shader build
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static int BuildShader(string vertSrc, string fragSrc)
        {
            int v = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(v, vertSrc);
            GL.CompileShader(v);
            CheckShader(v, "vertex");

            int f = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(f, fragSrc);
            GL.CompileShader(f);
            CheckShader(f, "fragment");

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, v);
            GL.AttachShader(prog, f);
            GL.LinkProgram(prog);

            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
                throw new InvalidOperationException("Shader link:\n" + GL.GetProgramInfoLog(prog));

            GL.DeleteShader(v);
            GL.DeleteShader(f);
            return prog;
        }

        private static void CheckShader(int s, string name)
        {
            GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
                throw new InvalidOperationException(
                    $"{name} shader:\n" + GL.GetShaderInfoLog(s));
        }
    }
}

