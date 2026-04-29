using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using CloudScope.Selection;
using CloudScope.Labeling;
using CloudScope.Rendering;

namespace CloudScope
{
    /// <summary>
    /// OpenGL 3.3 point-cloud viewer with interactive OBB labeling.
    ///
    /// Camera (always active):
    ///   Left drag        Orbit  — around scene or active box center
    ///   Right drag       Pan    — clicked point stays under cursor
    ///   Scroll           Zoom   — toward point under cursor
    ///   Space            Toggle orthographic / perspective
    ///   Num1 / 3 / 7     Front / Right / Top view
    ///   Num5             Isometric preset
    ///   Home             Reset view
    ///   +  /  -          Point size up / down
    ///
    /// Label mode  (T to toggle):
    ///   Left drag        Draw selection rectangle  →  enters Edit mode
    ///   Drag face arrow  Resize box on that axis
    ///   Drag corner      Resize box diagonally
    ///   Drag center dot  Move box
    ///   Drag ring        Rotate box (local X / Y / Z axis)
    ///   Enter            Confirm: label enclosed points with current label
    ///   Escape           Cancel box / exit label mode
    ///   Del              Clear last labeled selection
    ///   1 – 7            Active label: Ground / Building / Vegetation /
    ///                    Vehicle / Road / Water / Wire
    ///   G / S / R        Grab / Scale / Rotate via mouse drag (keyboard edit)
    ///   X / Y / Z        Constrain keyboard edit to axis
    ///
    /// Preview: enclosed points highlighted yellow while box is active.
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
        private int _uAlphaLine, _uAlphaSphere;

        // -- Camera ────────────────────────────────────────────────────────────
        private readonly OrbitCamera _cam = new();
        private float _cloudRadius = 50f;

        // -- Pivot indicator animation ----------------------------------------
        private float _pivotFade  = 0f;  // 0=hidden 1=fully visible (only during orbit)
        private float _pivotFlash = 0f;  // brief size pulse when pivot is picked
        private Vector3 _displayPivot = Vector3.Zero; // fixed world pos; only updated on orbit click, not on pan

        // ── Labeling state ────────────────────────────────────────────────────
        private InteractionMode _mode = InteractionMode.Navigate;

        private readonly BoxSelectionTool      _boxTool      = new();
        private readonly SphereSelectionTool   _sphereTool   = new();
        private readonly CylinderSelectionTool _cylinderTool = new();
        private readonly ISelectionTool[]      _tools;
        private readonly GizmoRendererBase[]   _renderers;
        private int            _activeToolIndex = 0;
        private ISelectionTool ActiveTool    => _tools[_activeToolIndex];

        private EditAction _pendingAction = EditAction.None;
        private bool _editDragging;

        private readonly LabelManager _labelManager = new();
        private string _currentLabel = "Ground";
        private PointData[]? _pointsCPU;
        private string _lasFilePath = "";

        private static readonly string[] LabelPresets =
            { "Ground", "Building", "Vegetation", "Vehicle", "Road", "Water", "Wire" };

        // ── Labeling renderers ────────────────────────────────────────────────
        private readonly BoxGizmoRenderer      _boxGizmoRenderer      = new();
        private readonly SphereGizmoRenderer   _sphereGizmoRenderer   = new();
        private readonly CylinderGizmoRenderer _cylinderGizmoRenderer = new();
        private readonly HighlightRenderer   _highlightRenderer   = new();

        // -- Mouse state ────────────────────────────────────────────────----------
        private int  _lastMX, _lastMY;
        private bool _leftDown, _rightDown, _middleDown;

        // -- Inertia velocities (screen pixels/frame) ────────────────-------------
        private float _orbitVelX, _orbitVelY;
        private float _panVelX,   _panVelY;

        // ── Selection preview (background thread, throttled) ─────────────────
        private float _previewTimer = 999f;       // time since last launch
        private const float PreviewInterval = 0.08f;  // ~12 fps
        private HashSet<int>? _previewIndices;    // latest result, set by background task
        private bool _previewDirty;               // true when _previewIndices needs GPU upload
        private volatile bool _previewRunning;    // background task in flight

        // ── Point rendering ───────────────────────────────────────────────────
        private float _pointSize = 1.5f;

        // ── Vertex shader ─────────────────────────────────────────────────────
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
uniform float uAlpha;

void main()
{
    FragColor = vec4(vColor, uAlpha);
}
";

        // ── Fragment shader for Sphere Impostor (Yellow Pivot) ────────────────
        private const string SphereFragSrc = @"
#version 330 core
out vec4 FragColor;
uniform float uAlpha;

void main()
{
    vec2 p = gl_PointCoord * 2.0 - vec2(1.0);
    float r2 = dot(p, p);
    if (r2 > 1.0) discard;

    // soft edge glow ring
    float edge = smoothstep(0.85, 1.0, sqrt(r2));

    float z = sqrt(1.0 - r2);
    vec3 normal = vec3(p.x, -p.y, z);
    vec3 lightDir = normalize(vec3(1.0, 1.5, 1.0));
    float diff = max(dot(normal, lightDir), 0.25);

    vec3 core  = vec3(1.0, 0.92, 0.2) * diff;
    vec3 glow  = vec3(1.0, 0.7, 0.0);
    vec3 color = mix(core, glow, edge);
    FragColor  = vec4(color, uAlpha);
}
";

        public PointCloudViewer(int width, int height)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height),
                Title      = "CloudScope - Point Cloud Viewer",
                APIVersion = new Version(3, 3),
                Profile    = ContextProfile.Core,
            })
        {
            _tools     = new ISelectionTool[]    { _boxTool, _sphereTool, _cylinderTool };
            _renderers = new GizmoRendererBase[] { _boxGizmoRenderer, _sphereGizmoRenderer, _cylinderGizmoRenderer };
        }

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

            _uAlphaLine   = GL.GetUniformLocation(_lineShader,   "uAlpha");
            _uAlphaSphere = GL.GetUniformLocation(_sphereShader, "uAlpha");

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _cam.SetViewportSize(Size.X, Size.Y);
        }

        /// <summary>Upload point cloud to GPU. Must be called before Run().</summary>
        public void LoadPointCloud(PointData[] pts, float cloudRadius = 50f)
        {
            _pointCount  = pts.Length;
            _cloudRadius = cloudRadius;
            _pointsCPU   = pts;  // keep CPU reference for selection resolution
            _cam.FitToCloud(cloudRadius);
            _labelManager.LabelsChanged += _highlightRenderer.MarkDirty;

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

        /// <summary>Set the source LAS path for label file I/O.</summary>
        public void SetLasFilePath(string path) => _lasFilePath = path;

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Keyboard (non-mouse controls)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            float dt = (float)args.Time;

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
            if (KeyboardState.IsKeyPressed(Keys.Home))     _cam.ResetView(_cloudRadius);

            // WASD FPS Movement
            float moveSpeed = _cam.NavigationScale * 2.0f * dt;
            bool shift = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
            if (shift) moveSpeed *= 5f;

            float dx = 0, dy = 0, dz = 0;
            if (KeyboardState.IsKeyDown(Keys.W)) dz -= moveSpeed;
            if (KeyboardState.IsKeyDown(Keys.S)) dz += moveSpeed;
            if (KeyboardState.IsKeyDown(Keys.A)) dx -= moveSpeed;
            if (KeyboardState.IsKeyDown(Keys.D)) dx += moveSpeed;
            if (KeyboardState.IsKeyDown(Keys.E)) dy += moveSpeed;
            if (KeyboardState.IsKeyDown(Keys.Q)) dy -= moveSpeed;

            if (dx != 0 || dy != 0 || dz != 0)
            {
                _cam.CancelTransition();
                _cam.MoveFPS(dx, dy, dz);
            }

            // Smooth camera transition tick (view presets, focus)
            _cam.TickTransition(dt);

            // Pivot: suppressed when any selection volume is active.
            bool toolActive   = _mode == InteractionMode.Label && ActiveTool.HasVolume;
            bool orbiting     = _leftDown || _orbitVelX != 0f || _orbitVelY != 0f;
            float pivotTarget = (orbiting && !toolActive) ? 1f : 0f;
            float pivotRate   = pivotTarget > _pivotFade ? 8f : 5f;
            _pivotFade += (pivotTarget - _pivotFade) * Math.Min(pivotRate * dt, 1f);
            if (_pivotFlash > 0f) _pivotFlash = Math.Max(0f, _pivotFlash - dt * 2.5f);

            // Live preview: resolve which points are inside the active selection volume.
            // ResolveSelection can iterate millions of points — run it on a background thread
            // so the render loop is never blocked.
            bool anyPreviewActive = _mode == InteractionMode.Label && ActiveTool.HasVolume;
            if (anyPreviewActive && _pointsCPU != null)
            {
                _previewTimer += dt;
                if (_previewTimer >= PreviewInterval && !_previewRunning)
                {
                    _previewTimer   = 0f;
                    _previewRunning = true;
                    var tool   = ActiveTool;
                    var points = _pointsCPU;
                    int vpW    = (int)_cam.ViewportWidth;
                    int vpH    = (int)_cam.ViewportHeight;
                    Task.Run(() =>
                    {
                        var result = tool.ResolveSelection(points, _cam, vpW, vpH);
                        _previewIndices = result;
                        _previewDirty   = true;
                        _previewRunning = false;
                    });
                }
            }
            else if (!anyPreviewActive && _previewIndices != null)
            {
                _previewIndices = null;
                _previewDirty   = true;
                _previewTimer   = 999f;
            }

            // Suppress inertia while a transition is playing (avoids fighting)
            if (_cam.IsTransitioning)
            {
                _orbitVelX = _orbitVelY = _panVelX = _panVelY = 0f;
            }
            else
            {
                // Orbit inertia
                if (!_leftDown && (_orbitVelX != 0f || _orbitVelY != 0f))
                {
                    _cam.Rotate(_orbitVelX, _orbitVelY);
                    _orbitVelX *= 0.60f; _orbitVelY *= 0.60f;
                    if (MathF.Abs(_orbitVelX) < 0.05f) _orbitVelX = 0f;
                    if (MathF.Abs(_orbitVelY) < 0.05f) _orbitVelY = 0f;
                }

                // Pan inertia
                if (!_rightDown && !_middleDown && (_panVelX != 0f || _panVelY != 0f))
                {
                    int cx = Size.X / 2, cy = Size.Y / 2;
                    int pdx = (int)MathF.Round(_panVelX), pdy = (int)MathF.Round(_panVelY);
                    if (pdx != 0 || pdy != 0)
                        _cam.Pan(cx, cy, cx + pdx, cy + pdy);
                    _panVelX *= 0.60f; _panVelY *= 0.60f;
                    if (MathF.Abs(_panVelX) < 0.05f) _panVelX = 0f;
                    if (MathF.Abs(_panVelY) < 0.05f) _panVelY = 0f;
                }
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

            _cam.CancelTransition();

            if (e.Button == MouseButton.Left)
            {
                bool toolConsumed = false;

                if (_mode == InteractionMode.Label)
                {
                    var tool = ActiveTool;
                    if (tool.Phase == ToolPhase.Idle)
                    {
                        tool.OnMouseDown(mx, my, _cam);
                        toolConsumed = true;
                    }
                    else if (tool.Phase == ToolPhase.Drawing)
                    {
                        toolConsumed = true; // block orbit during active draw
                    }
                    else if (tool.Phase == ToolPhase.Editing)
                    {
                        int h = tool.HitTestHandles(mx, my, _cam);
                        if (h >= 0)
                        {
                            tool.BeginHandleDrag(h, mx, my, _cam);
                            toolConsumed = true;
                        }
                        else if (_pendingAction != EditAction.None)
                        {
                            switch (_pendingAction)
                            {
                                case EditAction.Grab:   tool.BeginGrab(mx, my, _cam);   break;
                                case EditAction.Scale:  tool.BeginScale(mx, my, _cam);  break;
                                case EditAction.Rotate: tool.BeginRotate(mx, my, _cam); break;
                            }
                            _editDragging = true;
                            toolConsumed  = true;
                        }
                    }
                }

                // Orbit: always active unless tool consumed the click.
                if (!toolConsumed)
                {
                    if (_mode == InteractionMode.Label && ActiveTool.HasVolume)
                    {
                        _cam.SetOrbitPivot(ActiveTool.Center);
                        _displayPivot = ActiveTool.Center;
                    }
                    else if (_cam.SetOrbitPivotFromScreen(mx, my, 11))
                    {
                        _displayPivot = _cam.Pivot;
                        _pivotFlash = 1.0f;
                    }
                    _leftDown  = true;
                    _orbitVelX = 0f; _orbitVelY = 0f;
                }
            }

            if (e.Button == MouseButton.Right)
            {
                _cam.PickDepthWindow(mx, my, 11);
                _rightDown = true;
                _panVelX = 0f; _panVelY = 0f;
            }

            if (e.Button == MouseButton.Middle)
            {
                _cam.PickDepthWindow(mx, my, 11);
                _middleDown = true;
                _panVelX = 0f; _panVelY = 0f;
            }

            _lastMX = mx;
            _lastMY = my;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButton.Left)
            {
                if (_mode == InteractionMode.Label)
                {
                    var tool = ActiveTool;
                    if (tool.IsHandleDragging)
                    {
                        tool.EndHandleDrag();
                    }
                    else if (_editDragging)
                    {
                        tool.EndEdit();
                        _editDragging  = false;
                        _pendingAction = EditAction.None;
                    }
                    else if (tool.Phase == ToolPhase.Drawing)
                    {
                        int mx = (int)MouseState.Position.X;
                        int my = (int)MouseState.Position.Y;
                        if (tool is BoxSelectionTool box)
                        {
                            box.OnMouseUp(mx, my);
                            if (box.Phase == ToolPhase.Drawing)
                            {
                                box.FinalizeBoxFromScreen(_cam);
                                Console.WriteLine("Box drawn — drag the orange arrow (+Z) to extrude. Rings=rotate, corners=resize. Enter to label.");
                            }
                        }
                        else
                        {
                            tool.OnMouseUp(mx, my);
                            if (tool.IsEditing)
                                Console.WriteLine("Selection placed — drag orange arrow to set height. Enter to label.");
                        }
                    }
                }
                _leftDown = false;
            }

            if (e.Button == MouseButton.Right)  _rightDown  = false;
            if (e.Button == MouseButton.Middle) _middleDown = false;
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            int mx = (int)e.X;
            int my = (int)e.Y;
            int dx = mx - _lastMX;
            int dy = my - _lastMY;

            if (_mode == InteractionMode.Label)
            {
                var tool = ActiveTool;
                if (tool.Phase == ToolPhase.Drawing)
                    tool.OnMouseMove(mx, my, _cam);
                else if (tool.IsHandleDragging)
                    tool.UpdateHandleDrag(mx, my, _cam);
                else if (_editDragging)
                    tool.UpdateEdit(mx, my, _cam);
                else if (tool.Phase == ToolPhase.Editing)
                    tool.HoveredHandle = tool.HitTestHandles(mx, my, _cam);
            }

            // Camera navigation — always active in navigate mode,
            // and in label mode whenever tool didn't consume the left button
            if (_leftDown)
            {
                _cam.Rotate(dx, dy);
                _orbitVelX = dx; _orbitVelY = dy;
            }
            else if (_rightDown || _middleDown)
            {
                _cam.Pan(_lastMX, _lastMY, mx, my);
                _panVelX = mx - _lastMX; _panVelY = my - _lastMY;
            }

            _lastMX = mx;
            _lastMY = my;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            int mx = (int)MouseState.Position.X;
            int my = (int)MouseState.Position.Y;

            _cam.PickDepthWindow(mx, my, 11);

            // Adaptive zoom: larger steps when zoomed out, finer when zoomed in
            float zoomRatio = Math.Clamp((float)(_cam.Hvs / _cloudRadius), 0.1f, 5f);
            float step = Math.Clamp(zoomRatio * 0.25f, 0.1f, 0.5f);
            float factor = e.OffsetY > 0 ? 1f + step : 1f / (1f + step);
            _cam.Zoom(mx, my, factor);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool ctrl = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);

            // ── Labeling shortcuts ────────────────────────────────────────────
            if (e.Key == Keys.L)
            {
                _mode = _mode == InteractionMode.Navigate
                    ? InteractionMode.Label : InteractionMode.Navigate;
                ActiveTool.Cancel();
                _pendingAction = EditAction.None;
                _editDragging  = false;
                Console.WriteLine($"Mode: {_mode}  (tool: {ActiveTool.ToolType}, label: '{_currentLabel}')");
            }

            if (e.Key == Keys.Escape && _mode == InteractionMode.Label)
            {
                if (_editDragging)
                {
                    ActiveTool.EndEdit();
                    _editDragging  = false;
                    _pendingAction = EditAction.None;
                }
                else if (ActiveTool.IsEditing || ActiveTool.IsActive)
                {
                    ActiveTool.Cancel();
                    _pendingAction = EditAction.None;
                    Console.WriteLine("Selection cancelled");
                }
                else
                {
                    _mode = InteractionMode.Navigate;
                    Console.WriteLine("Mode: Navigate");
                }
                return;
            }

            if (_mode == InteractionMode.Label)
            {
                // ── Enter: confirm selection and apply label ──────────────────
                if (e.Key == Keys.Enter && ActiveTool.IsEditing)
                {
                    ActiveTool.Confirm();
                        if (_pointsCPU != null)
                        {
                            var selected = ActiveTool.ResolveSelection(
                                _pointsCPU, _cam, Size.X, Size.Y);
                            if (selected.Count > 0)
                            {
                                _labelManager.ApplyLabel(selected, _currentLabel);
                                Console.WriteLine($"Labeled {selected.Count} points as '{_currentLabel}'  (total: {_labelManager.Count})");
                            }
                            else
                            {
                                Console.WriteLine("No points in selection volume");
                            }
                        }
                }

                // ── G/S/R: Set pending edit action (Blender-style) ───────────
                if (ActiveTool.IsEditing)
                {
                    if (e.Key == Keys.G) { _pendingAction = EditAction.Grab;   Console.WriteLine("Grab — left-click + drag to move"); }
                    if (e.Key == Keys.S) { _pendingAction = EditAction.Scale;  Console.WriteLine("Scale — left-click + drag to resize"); }
                    if (e.Key == Keys.R) { _pendingAction = EditAction.Rotate; Console.WriteLine("Rotate — left-click + drag to rotate"); }

                    if (e.Key == Keys.X) { ActiveTool.SetAxisConstraint(0); Console.WriteLine("Constraint: X axis"); }
                    if (e.Key == Keys.Y) { ActiveTool.SetAxisConstraint(1); Console.WriteLine("Constraint: Y axis"); }
                    if (e.Key == Keys.Z) { ActiveTool.SetAxisConstraint(2); Console.WriteLine("Constraint: Z axis"); }
                }

                // ── Tool switch ──────────────────────────────────────────────
                if (!ActiveTool.IsEditing && !ActiveTool.IsActive)
                {
                    if (e.Key == Keys.D1) { _activeToolIndex = 0; Console.WriteLine("Tool: Box"); }
                    if (e.Key == Keys.D2) { _activeToolIndex = 1; Console.WriteLine("Tool: Sphere"); }
                    if (e.Key == Keys.D3) { _activeToolIndex = 2; Console.WriteLine("Tool: Cylinder"); }
                }

                // Label presets (4-9 → index 0-5; D3 is now Cylinder tool)
                Keys[] presetKeys = { Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9 };
                for (int i = 0; i < presetKeys.Length && i < LabelPresets.Length; i++)
                {
                    if (e.Key == presetKeys[i])
                    {
                        _currentLabel = LabelPresets[i];
                        Console.WriteLine($"Label: '{_currentLabel}'");
                    }
                }

                // Undo
                if (ctrl && e.Key == Keys.Z)
                {
                    if (_labelManager.Undo())
                        Console.WriteLine($"Undo — labels: {_labelManager.Count}");
                }

                // Save / Load
                if (ctrl && e.Key == Keys.S && !string.IsNullOrEmpty(_lasFilePath))
                    LabelFileIO.Save(_lasFilePath, _labelManager);
                if (ctrl && e.Key == Keys.O && !string.IsNullOrEmpty(_lasFilePath))
                    LabelFileIO.Load(_lasFilePath, _labelManager);
            }

            // ── Camera shortcuts (always available) ───────────────────────────
            if (e.Key == Keys.Space)
            {
                int mx = (int)MouseState.Position.X;
                int my = (int)MouseState.Position.Y;
                _cam.PickDepthWindow(mx, my, 11);
                _cam.ToggleProjection(mx, my);
            }

            if (e.Key == Keys.F)
            {
                int mx = (int)MouseState.Position.X;
                int my = (int)MouseState.Position.Y;
                _cam.FocusOnCursor(mx, my);
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

            // 1. Draw point cloud — CLOD: draw fewer points when zoomed far out.
            // Points were shuffled at load time so the first K form a uniform spatial sample.
            if (_pointCount > 0)
            {
                float lodRatio = (float)(_cloudRadius / Math.Max(_cam.Hvs, _cloudRadius * 0.001));
                float lodFactor = Math.Clamp(lodRatio * lodRatio, 0.005f, 1f);
                int drawCount = Math.Max((int)(_pointCount * lodFactor), Math.Min(100_000, _pointCount));
                GL.Uniform1(_uPointSize, _pointSize);
                GL.BindVertexArray(_vao);
                GL.DrawArrays(PrimitiveType.Points, 0, drawCount);
            }

            // 2. Labeled points highlight (second pass with label colors)
            if (_pointsCPU != null && _labelManager.Count > 0)
                _highlightRenderer.Render(_pointsCPU, _labelManager, ref view, ref proj, _pointSize);

            // 2b. Box selection preview — points currently inside the box, shown in yellow.
            // GPU upload happens here (not in OnUpdateFrame) to keep GL calls on the render thread.
            if (_previewDirty)
            {
                _highlightRenderer.UpdatePreview(_pointsCPU, _previewIndices);
                _previewDirty = false;
            }
            _highlightRenderer.RenderPreview(ref view, ref proj, _pointSize);

            // 3. Active selection tool gizmo
            if (_mode == InteractionMode.Label)
            {
                var tool = ActiveTool;
                if (tool is BoxSelectionTool box && box.Phase == ToolPhase.Drawing)
                {
                    _boxGizmoRenderer.RenderPlacementRect(box.StartX, box.StartY, box.EndX, box.EndY, Size.X, Size.Y);
                }
                else if (tool.HasVolume || tool.Phase == ToolPhase.Drawing)
                {
                    _renderers[_activeToolIndex].Render(tool, view, proj, _cam);
                }
            }

            // 4. Pivot indicator — only during orbit; hidden when any selection volume is active
            bool anyToolActive = _mode == InteractionMode.Label && ActiveTool.HasVolume;
            if (!anyToolActive && (_pivotFade > 0.01f || _pivotFlash > 0.01f))
                RenderPivotIndicator(ref view, ref proj);

            // 5. Center crosshair — fades out as pivot fades in, full when pivot is hidden
            float crossAlpha = 1f - _pivotFade;
            if (crossAlpha > 0.01f)
                RenderCenterCrosshair(crossAlpha);

            // 6. Mode indicator (label mode banner)
            if (_mode == InteractionMode.Label)
                RenderModeIndicator(ref view, ref proj);

            SwapBuffers();
        }

        private int _pivotVao = -1;
        private int _pivotVbo = -1;
        private int _pivotVertexCount = 0;

        private void RenderPivotIndicator(ref Matrix4 view, ref Matrix4 proj)
        {
            if (_pivotVao == -1)
            {
                const int segments = 96;
                // 3 short axes (±0.55) + 3 circles @ 96 segments = 6 + 576 = 582 verts
                _pivotVertexCount = 6 + 3 * segments * 2;
                float[] pivotData = new float[_pivotVertexCount * 6];
                int idx = 0;

                void AddV(float x, float y, float z, float r, float g, float b)
                {
                    pivotData[idx++] = x; pivotData[idx++] = y; pivotData[idx++] = z;
                    pivotData[idx++] = r; pivotData[idx++] = g; pivotData[idx++] = b;
                }

                // Short axis stubs — 55 % of circle radius, slightly brightened
                const float ax = 0.55f;
                AddV(-ax, 0f, 0f, 1f, 0.3f, 0.3f); AddV(ax, 0f, 0f, 1f, 0.3f, 0.3f);
                AddV(0f, -ax, 0f, 0.3f, 1f, 0.3f); AddV(0f, ax, 0f, 0.3f, 1f, 0.3f);
                AddV(0f, 0f, -ax, 0.3f, 0.5f, 1f); AddV(0f, 0f, ax, 0.3f, 0.5f, 1f);

                // Orbit circles — softer, slightly desaturated colours
                float step = MathF.PI * 2f / segments;
                (float r, float g, float b)[] cols = {
                    (1f, 0.35f, 0.35f), // YZ — warm red
                    (0.35f, 1f, 0.35f), // XZ — green
                    (0.35f, 0.6f,  1f), // XY — cool blue
                };
                for (int c = 0; c < 3; c++)
                {
                    var (cr, cg, cb) = cols[c];
                    for (int i = 0; i < segments; i++)
                    {
                        float a1 = i * step, a2 = (i + 1) * step;
                        float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                        float c2 = MathF.Cos(a2), s2 = MathF.Sin(a2);
                        if (c == 0) { AddV(0, c1, s1, cr, cg, cb); AddV(0, c2, s2, cr, cg, cb); }
                        else if (c == 1) { AddV(c1, 0, s1, cr, cg, cb); AddV(c2, 0, s2, cr, cg, cb); }
                        else             { AddV(c1, s1, 0, cr, cg, cb); AddV(c2, s2, 0, cr, cg, cb); }
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

            // Effective alpha: fade + flash boost, sphere gets extra size during flash
            float eff        = Math.Clamp(_pivotFade + _pivotFlash, 0f, 1f);
            float spherePx   = 11f + 11f * _pivotFade + _pivotFlash * 14f;

            Vector3 p     = _displayPivot;
            float   scale = _cam.PivotIndicatorScaleAt(p);
            Matrix4 model = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(p);
            Matrix4 mv    = model * view;

            // ── Lines ─────────────────────────────────────────────────────────
            GL.UseProgram(_lineShader);
            GL.UniformMatrix4(_uViewLine, false, ref mv);
            GL.UniformMatrix4(_uProjLine, false, ref proj);
            GL.BindVertexArray(_pivotVao);

            // Pass 1 — depth-tested: rings are properly embedded in 3D space
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Uniform1(_uAlphaLine, eff);
            GL.LineWidth(1f + eff);
            GL.DrawArrays(PrimitiveType.Lines, 0, _pivotVertexCount);

            // Pass 2 — X-ray ghost: occluded portions remain faintly visible
            GL.Disable(EnableCap.DepthTest);
            GL.Uniform1(_uAlphaLine, eff * 0.20f);
            GL.LineWidth(1.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, _pivotVertexCount);

            GL.DepthMask(true);

            // ── Sphere impostor — always on top ───────────────────────────────
            if (_sphereVao == -1)
            {
                _sphereVao = GL.GenVertexArray();
                _sphereVbo = GL.GenBuffer();
                GL.BindVertexArray(_sphereVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
                GL.EnableVertexAttribArray(0);
            }

            float[] sphereData = { p.X, p.Y, p.Z };
            GL.BindVertexArray(_sphereVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 3 * sizeof(float), sphereData, BufferUsageHint.DynamicDraw);

            GL.UseProgram(_sphereShader);
            GL.UniformMatrix4(_uViewSphere,       false, ref view);
            GL.UniformMatrix4(_uProjSphere,       false, ref proj);
            GL.Uniform1(_uPointSizeSphere, spherePx);
            GL.Uniform1(_uAlphaSphere,     eff);

            GL.DrawArrays(PrimitiveType.Points, 0, 1);
            GL.Enable(EnableCap.DepthTest);
        }

        private int _sphereVao = -1;
        private int _sphereVbo = -1;

        private int _crosshairVao = -1;
        private int _crosshairVbo = -1;

        // Pre-allocated scratch buffers for screen-space renderers — avoids per-frame heap allocations.
        private readonly float[] _crossData   = new float[24]; // 4 verts × 6 floats (pos+col)
        private readonly float[] _shadowData  = new float[24];
        private readonly float[] _indData     = new float[24];

        private void RenderCenterCrosshair(float alpha = 1.0f)
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

            _crossData[0]=-sX; _crossData[1]=0f;  _crossData[2]=0f;  _crossData[3]=g; _crossData[4]=g; _crossData[5]=g;
            _crossData[6]= sX; _crossData[7]=0f;  _crossData[8]=0f;  _crossData[9]=g; _crossData[10]=g; _crossData[11]=g;
            _crossData[12]=0f; _crossData[13]=-sY; _crossData[14]=0f; _crossData[15]=g; _crossData[16]=g; _crossData[17]=g;
            _crossData[18]=0f; _crossData[19]= sY; _crossData[20]=0f; _crossData[21]=g; _crossData[22]=g; _crossData[23]=g;
            float[] crossData = _crossData;

            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, crossData.Length * sizeof(float), crossData, BufferUsageHint.DynamicDraw);

            GL.UseProgram(_lineShader);
            GL.Uniform1(_uAlphaLine, alpha * 0.55f); // shadow pass uses reduced alpha
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.UniformMatrix4(_uProjLine, false, ref ident);

            GL.Disable(EnableCap.DepthTest);

            // Shadow pass (subtle dark offset)
            Matrix4 shadow = Matrix4.CreateTranslation(1f/Size.X, -1f/Size.Y, 0);
            GL.UniformMatrix4(_uViewLine, false, ref shadow);
            _shadowData[0]=-sX; _shadowData[1]=0f;  _shadowData[2]=0f;  _shadowData[3]=0f; _shadowData[4]=0f; _shadowData[5]=0f;
            _shadowData[6]= sX; _shadowData[7]=0f;  _shadowData[8]=0f;  _shadowData[9]=0f; _shadowData[10]=0f; _shadowData[11]=0f;
            _shadowData[12]=0f; _shadowData[13]=-sY; _shadowData[14]=0f; _shadowData[15]=0f; _shadowData[16]=0f; _shadowData[17]=0f;
            _shadowData[18]=0f; _shadowData[19]= sY; _shadowData[20]=0f; _shadowData[21]=0f; _shadowData[22]=0f; _shadowData[23]=0f;
            GL.BufferData(BufferTarget.ArrayBuffer, _shadowData.Length * sizeof(float), _shadowData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);

            // Main crosshair
            GL.Uniform1(_uAlphaLine, alpha);
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.BufferData(BufferTarget.ArrayBuffer, crossData.Length * sizeof(float), crossData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);

            GL.Enable(EnableCap.DepthTest);
        }

        /// <summary>Renders a small colored dot in the top-left corner as label mode indicator.</summary>
        private void RenderModeIndicator(ref Matrix4 view, ref Matrix4 proj)
        {
            // Small colored dot at top-left to indicate label mode
            // Use the line shader with a single point
            float dotX = -1f + 30f / Size.X;
            float dotY =  1f - 30f / Size.Y;
            float r = 0f, g = 0.8f, b = 1f; // cyan for label mode

            if (ActiveTool.ToolType == SelectionToolType.Sphere)
            { r = 1f;    g = 0.6f;  b = 0.15f; } // orange for sphere tool
            else if (ActiveTool.ToolType == SelectionToolType.Cylinder)
            { r = 0.60f; g = 0.25f; b = 1f;    } // purple for cylinder tool

            GL.UseProgram(_lineShader);
            GL.Uniform1(_uAlphaLine, 0.9f);
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.UniformMatrix4(_uProjLine, false, ref ident);

            GL.Disable(EnableCap.DepthTest);

            // Draw a small crosshair-like indicator
            float sz = 8f / Size.X;
            float szy = 8f / Size.Y;
            _indData[0]=dotX-sz; _indData[1]=dotY;     _indData[2]=0f; _indData[3]=r; _indData[4]=g; _indData[5]=b;
            _indData[6]=dotX+sz; _indData[7]=dotY;     _indData[8]=0f; _indData[9]=r; _indData[10]=g; _indData[11]=b;
            _indData[12]=dotX;   _indData[13]=dotY-szy; _indData[14]=0f; _indData[15]=r; _indData[16]=g; _indData[17]=b;
            _indData[18]=dotX;   _indData[19]=dotY+szy; _indData[20]=0f; _indData[21]=r; _indData[22]=g; _indData[23]=b;

            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _indData.Length * sizeof(float), _indData, BufferUsageHint.DynamicDraw);
            GL.LineWidth(2.5f);
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

            // Labeling renderers
            _boxGizmoRenderer.Dispose();
            _sphereGizmoRenderer.Dispose();
            _cylinderGizmoRenderer.Dispose();
            _highlightRenderer.Dispose();

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

