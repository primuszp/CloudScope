using System;
using System.Collections.Generic;
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
        // ── Rendering ─────────────────────────────────────────────────────────
        private readonly PointCloudRenderer _pointRenderer = new();
        private readonly OverlayRenderer _overlayRenderer = new();
        private readonly FrameTimingDiagnostics _frameTiming = new();

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
        private readonly SelectionPreviewWorker _previewWorker = new();
        private bool _previewWasActive;

        // ── Point rendering ───────────────────────────────────────────────────
        private float _pointSize = 1.5f;

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

            _pointRenderer.Initialize();
            _overlayRenderer.Initialize();

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _cam.SetViewportSize(Size.X, Size.Y);
        }

        /// <summary>Upload point cloud to GPU. Must be called before Run().</summary>
        public void LoadPointCloud(PointData[] pts, float cloudRadius = 50f)
        {
            _cloudRadius = cloudRadius;
            _pointsCPU   = pts;  // keep CPU reference for selection resolution
            _cam.FitToCloud(cloudRadius);
            _labelManager.LabelsChanged += _highlightRenderer.MarkDirty;
            _pointRenderer.Upload(pts);
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
                _previewWasActive = true;
                _previewTimer += dt;
                if (_previewTimer >= PreviewInterval)
                {
                    _previewTimer   = 0f;
                    _previewWorker.Request(ActiveTool.CreateQuery(), _pointsCPU);
                }
            }
            else if (_previewWasActive)
            {
                _previewWorker.Clear();
                _previewTimer   = 999f;
                _previewWasActive = false;
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
                    if (_pointsCPU != null)
                    {
                        var selected = ActiveTool.CreateQuery().Resolve(_pointsCPU);
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
                    ActiveTool.Confirm();
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
            _frameTiming.BeginFrame();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var view = _cam.GetViewMatrix();
            var proj = _cam.GetProjectionMatrix();

            int drawCount = _pointRenderer.Render(ref view, ref proj, _pointSize, _cam.Hvs, _cloudRadius);
            _frameTiming.MarkMainDraw(drawCount);

            // 2. Labeled points highlight (second pass with label colors)
            if (_pointsCPU != null && _labelManager.Count > 0)
                _highlightRenderer.Render(_pointsCPU, _labelManager, ref view, ref proj, _pointSize);
            _frameTiming.MarkHighlight();

            // 2b. Box selection preview — points currently inside the box, shown in yellow.
            // GPU upload happens here (not in OnUpdateFrame) to keep GL calls on the render thread.
            if (_previewWorker.TryTakeLatest(out var previewIndices))
            {
                _highlightRenderer.UpdatePreview(_pointsCPU, previewIndices);
            }
            _highlightRenderer.RenderPreview(ref view, ref proj, _pointSize);
            _frameTiming.MarkPreview();

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
            _frameTiming.MarkGizmo();

            // 4. Pivot indicator — only during orbit; hidden when any selection volume is active
            bool anyToolActive = _mode == InteractionMode.Label && ActiveTool.HasVolume;
            if (!anyToolActive && (_pivotFade > 0.01f || _pivotFlash > 0.01f))
                _overlayRenderer.RenderPivotIndicator(ref view, ref proj, _cam, _displayPivot, _pivotFade, _pivotFlash);

            // 5. Center crosshair — fades out as pivot fades in, full when pivot is hidden
            float crossAlpha = 1f - _pivotFade;
            if (crossAlpha > 0.01f)
                _overlayRenderer.RenderCenterCrosshair(Size.X, Size.Y, crossAlpha);

            // 6. Mode indicator (label mode banner)
            if (_mode == InteractionMode.Label)
                _overlayRenderer.RenderModeIndicator(Size.X, Size.Y, ActiveTool.ToolType);

            SwapBuffers();
            _frameTiming.MarkSwapAndLog(args.Time);
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
            _pointRenderer.Dispose();
            _overlayRenderer.Dispose();

            // Labeling renderers
            _boxGizmoRenderer.Dispose();
            _sphereGizmoRenderer.Dispose();
            _cylinderGizmoRenderer.Dispose();
            _highlightRenderer.Dispose();
            _previewWorker.Dispose();

            base.OnUnload();
        }

    }
}


