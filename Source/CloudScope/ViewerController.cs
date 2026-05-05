using System;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;

namespace CloudScope
{
    /// <summary>
    /// OpenTK/GameWindow host for the OpenGL point-cloud viewer.
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
    public sealed class ViewerController : IDisposable
    {
        // ── Rendering ─────────────────────────────────────────────────────────
        private readonly IRenderBackend _renderBackend;
        private readonly IPointCloudRenderer _pointRenderer;
        private readonly IOverlayRenderer _overlayRenderer;
        private readonly IHighlightRenderer _highlightRenderer;
        private readonly SelectionGizmoRenderers _selectionGizmoRenderers;
        private readonly FrameTimingDiagnostics _frameTiming = new();
        private readonly SelectionController _selection;

        // -- Camera ────────────────────────────────────────────────────────────
        private readonly OrbitCamera _cam = new();
        private float _cloudRadius = 50f;

        private readonly CameraInputController _cameraInput = new();

        private readonly ISelectionGizmoRenderer[]   _renderers;

        private int _width;
        private int _height;

        public ViewerController(int width, int height, IRenderBackend renderBackend)
        {
            _width = width;
            _height = height;
            _renderBackend = renderBackend;
            _pointRenderer = renderBackend.CreatePointCloudRenderer();
            _overlayRenderer = renderBackend.CreateOverlayRenderer();
            _highlightRenderer = renderBackend.CreateHighlightRenderer();
            _selectionGizmoRenderers = renderBackend.CreateSelectionGizmoRenderers();
            _selection = new SelectionController(_highlightRenderer.MarkDirty);
            _cam.SetDepthPicker(renderBackend.CreateDepthPicker());
            _renderers = _selectionGizmoRenderers.All;
        }

        public void Load()
        {
            _renderBackend.InitializeFrameState();
            _pointRenderer.Initialize();
            _overlayRenderer.Initialize();

            _cam.SetViewportSize(_width, _height);
        }

        /// <summary>Upload point cloud to GPU. Must be called before Run().</summary>
        public void LoadPointCloud(PointData[] pts, float cloudRadius = 50f)
        {
            _cloudRadius = cloudRadius;
            _selection.LoadPointCloud(pts);
            _cam.FitToCloud(cloudRadius);
            _pointRenderer.Upload(pts);
        }

        /// <summary>Set the source LAS path for label file I/O.</summary>
        public void SetLasFilePath(string path) => _selection.SetLasFilePath(path);

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Keyboard (non-mouse controls)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public bool UpdateFrame(float dt, IViewerKeyboard keyboard)
        {
            bool shouldClose = _cameraInput.UpdateFrame(
                dt,
                keyboard,
                _cam,
                _cloudRadius,
                _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume,
                _width,
                _height);

            _selection.UpdatePreview(dt);

            return shouldClose;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Mouse â€” mirrors AdvancedZPR event handler order exactly
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public void MouseDown(ViewerMouseButton button, int mx, int my)
        {

            _cam.CancelTransition();

            if (button == ViewerMouseButton.Left)
            {
                bool toolConsumed = _selection.MouseDownLeft(mx, my, _cam);

                _cameraInput.MouseDown(
                    button,
                    mx,
                    my,
                    _cam,
                    toolConsumed,
                    _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume,
                    _selection.ActiveTool.Center);
            }

            if (button != ViewerMouseButton.Left)
                _cameraInput.MouseDown(button, mx, my, _cam, false, false, Vector3.Zero);
        }

        public void MouseUp(ViewerMouseButton button, int mx, int my)
        {

            if (button == ViewerMouseButton.Left)
            {
                _selection.MouseUpLeft(mx, my, _cam);
            }

            _cameraInput.MouseUp(button);
        }

        public void MouseMove(int mx, int my)
        {
            _selection.MouseMove(mx, my, _cam);

            _cameraInput.MouseMove(mx, my, _cam);
        }

        public void MouseWheel(int mx, int my, float offsetY)
        {
            _cameraInput.MouseWheel(mx, my, offsetY, _cam, _cloudRadius);
        }

        public void KeyDown(ViewerKey key, bool ctrl, int mouseX, int mouseY)
        {

            _selection.KeyDown(key, ctrl);

            // ── Camera shortcuts (always available) ───────────────────────────
            if (key == ViewerKey.Space)
            {
                _cam.PickDepthWindow(mouseX, mouseY, 11);
                _cam.ToggleProjection(mouseX, mouseY);
            }

            if (key == ViewerKey.F)
            {
                _cam.FocusOnCursor(mouseX, mouseY);
            }
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        public void RenderFrame(double frameTime)
        {
            _frameTiming.BeginFrame();

            _renderBackend.BeginFrame();

            var view = _cam.GetViewMatrix();
            var proj = _cam.GetProjectionMatrix();

            int drawCount = _pointRenderer.Render(ref view, ref proj, _cameraInput.PointSize, _cam.Hvs, _cloudRadius);
            _frameTiming.MarkMainDraw(drawCount);

            // 2. Labeled points highlight (second pass with label colors)
            if (_selection.Points != null && _selection.Labels.Count > 0)
                _highlightRenderer.Render(_selection.Points, _selection.Labels, ref view, ref proj, _cameraInput.PointSize);
            _frameTiming.MarkHighlight();

            // 2b. Box selection preview — points currently inside the box, shown in yellow.
            // GPU upload happens here (not in OnUpdateFrame) to keep GL calls on the render thread.
            if (_selection.TryTakePreview(out var previewIndices))
            {
                _highlightRenderer.UpdatePreview(_selection.Points, previewIndices);
            }
            _highlightRenderer.RenderPreview(ref view, ref proj, _cameraInput.PointSize);
            _frameTiming.MarkPreview();

            // 3. Active selection tool gizmo
            if (_selection.Mode == InteractionMode.Label)
            {
                var tool = _selection.ActiveTool;
                if (tool is BoxSelectionTool box && box.Phase == ToolPhase.Drawing)
                {
                    _selectionGizmoRenderers.Box.RenderPlacementRect(box.StartX, box.StartY, box.EndX, box.EndY, _width, _height);
                }
                else if (tool.HasVolume || tool.Phase == ToolPhase.Drawing)
                {
                    _renderers[_selection.ActiveToolIndex].Render(tool, view, proj, _cam);
                }
            }
            _frameTiming.MarkGizmo();

            // 4. Pivot indicator — only during orbit; hidden when any selection volume is active
            bool anyToolActive = _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume;
            if (!anyToolActive && (_cameraInput.PivotFade > 0.01f || _cameraInput.PivotFlash > 0.01f))
                _overlayRenderer.RenderPivotIndicator(ref view, ref proj, _cam, _cameraInput.DisplayPivot, _cameraInput.PivotFade, _cameraInput.PivotFlash);

            // 5. Center crosshair — fades out as pivot fades in, full when pivot is hidden
            float crossAlpha = 1f - _cameraInput.PivotFade;
            if (crossAlpha > 0.01f)
                _overlayRenderer.RenderCenterCrosshair(_width, _height, crossAlpha);

            // 6. Mode indicator (label mode banner)
            if (_selection.Mode == InteractionMode.Label)
                _overlayRenderer.RenderModeIndicator(_width, _height, _selection.ActiveTool.ToolType);

            _frameTiming.MarkSwapAndLog(frameTime);
        }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            _renderBackend.Resize(width, height);
            _cam.SetViewportSize(width, height);
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            _pointRenderer.Dispose();
            _overlayRenderer.Dispose();

            // Labeling renderers
            foreach (var renderer in _renderers)
                renderer.Dispose();
            _highlightRenderer.Dispose();
            _selection.Dispose();

        }

    }
}


