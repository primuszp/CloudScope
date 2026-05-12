using System;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;

namespace CloudScope
{
    public sealed class ViewerController : IDisposable
    {
        private readonly IRenderFrameLifecycle _frameLifecycle;
        private readonly IPointCloudRenderer _pointRenderer;
        private readonly IOverlayRenderer _overlayRenderer;
        private readonly IHighlightRenderer _highlightRenderer;
        private readonly SelectionGizmoRenderers _selectionGizmoRenderers;
        private readonly FrameTimingDiagnostics _frameTiming = new();
        private readonly SelectionController _selection;

        private readonly OrbitCamera _cam = new();
        private float _cloudRadius = 50f;

        private readonly CameraInputController _cameraInput = new();
        private readonly bool _metalFrameLog = true; // FORCE LOG ON
        private int _metalFrameLogCounter;

        private int _width;
        private int _height;

        public ViewerController(int width, int height, IRenderBackend renderBackend)
        {
            _width = width;
            _height = height;
            _frameLifecycle = renderBackend;
            _pointRenderer = renderBackend.CreatePointCloudRenderer();
            _overlayRenderer = renderBackend.CreateOverlayRenderer();
            _highlightRenderer = renderBackend.CreateHighlightRenderer();
            _selectionGizmoRenderers = renderBackend.CreateSelectionGizmoRenderers();
            _selection = new SelectionController(_highlightRenderer.MarkDirty);
            _cam.SetDepthPicker(renderBackend.CreateDepthPicker());
        }

        public void Load()
        {
            _frameLifecycle.InitializeFrameState();
            _pointRenderer.Initialize();
            _overlayRenderer.Initialize();
            _cam.SetViewportSize(_width, _height);
        }

        public void LoadPointCloud(PointData[] pts, float cloudRadius = 50f)
        {
            _cloudRadius = cloudRadius;
            _selection.LoadPointCloud(pts);
            _cam.FitToCloud(cloudRadius);
            _pointRenderer.Upload(pts);
        }

        public void SetLasFilePath(string path) => _selection.SetLasFilePath(path);

        public bool NeedsContinuousFrames => _cameraInput.NeedsContinuousFrames;

        public bool UpdateFrame(float dt, IViewerKeyboard keyboard)
        {
            bool shouldClose = _cameraInput.UpdateFrame(dt, keyboard, _cam, _cloudRadius, _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume, _width, _height);
            _selection.UpdatePreview(dt);
            return shouldClose;
        }

        public void MouseDown(ViewerMouseButton button, int mx, int my)
        {
            _cam.CancelTransition();
            if (button == ViewerMouseButton.Left)
            {
                bool toolConsumed = _selection.MouseDownLeft(mx, my, _cam);
                _cameraInput.MouseDown(button, mx, my, _cam, toolConsumed, _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume, _selection.ActiveTool.Center);
            }
            if (button != ViewerMouseButton.Left)
                _cameraInput.MouseDown(button, mx, my, _cam, false, false, Vector3.Zero);
        }

        public void MouseUp(ViewerMouseButton button, int mx, int my)
        {
            if (button == ViewerMouseButton.Left) _selection.MouseUpLeft(mx, my, _cam);
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
            if (key == ViewerKey.Space)
            {
                _cam.PickDepthWindow(mouseX, mouseY, 11);
                _cam.ToggleProjection(mouseX, mouseY);
            }
            if (key == ViewerKey.F) _cam.FocusOnCursor(mouseX, mouseY);
        }

        public void RenderFrame(double frameTime)
        {
            _frameTiming.BeginFrame();
            using var frame = _frameLifecycle.BeginFrame();
            var frameData = frame.FrameData;

            var view = _cam.GetViewMatrix();
            var proj = _cam.GetProjectionMatrix();

            int drawCount = _pointRenderer.Render(frameData, ref view, ref proj, _cameraInput.PointSize, _cam.Hvs, _cloudRadius);
            _frameTiming.MarkMainDraw(drawCount);

            if (_metalFrameLog && (++_metalFrameLogCounter % 60) == 0)
                Console.WriteLine($"[Cam] Pivot: {_cam.Pivot.X:F1}, {_cam.Pivot.Y:F1}, {_cam.Pivot.Z:F1} | Hvs: {_cam.Hvs:F2} | Draw: {drawCount}");

            if (_selection.Points != null && _selection.Labels.Count > 0)
                _highlightRenderer.Render(frameData, _selection.Points, _selection.Labels, ref view, ref proj, _cameraInput.PointSize);
            _frameTiming.MarkHighlight();

            if (_selection.TryTakePreview(out var previewIndices))
                _highlightRenderer.UpdatePreview(_selection.Points, previewIndices);
            _highlightRenderer.RenderPreview(frameData, ref view, ref proj, _cameraInput.PointSize);
            _frameTiming.MarkPreview();

            if (_selection.Mode == InteractionMode.Label)
            {
                var tool = _selection.ActiveTool;
                if (!_selectionGizmoRenderers.TryRenderPlacement(frameData, tool, _width, _height)
                    && (tool.HasVolume || tool.Phase == ToolPhase.Drawing))
                {
                    _selectionGizmoRenderers.Render(frameData, tool, view, proj, _cam);
                }
            }
            _frameTiming.MarkGizmo();

            bool anyToolActive = _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume;
            if (!anyToolActive && (_cameraInput.PivotFade > 0.01f || _cameraInput.PivotFlash > 0.01f))
                _overlayRenderer.RenderPivotIndicator(frameData, ref view, ref proj, _cam, _cameraInput.DisplayPivot, _cameraInput.PivotFade, _cameraInput.PivotFlash);

            float crossAlpha = 1f - _cameraInput.PivotFade;
            if (crossAlpha > 0.01f)
                _overlayRenderer.RenderCenterCrosshair(frameData, _width, _height, crossAlpha);

            if (_selection.Mode == InteractionMode.Label)
                _overlayRenderer.RenderModeIndicator(frameData, _width, _height, _selection.ActiveTool.ToolType);

            _frameTiming.MarkSwapAndLog(frameTime);
        }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            _frameLifecycle.Resize(width, height);
            _cam.SetViewportSize(width, height);
        }

        public void Dispose()
        {
            _pointRenderer.Dispose();
            _overlayRenderer.Dispose();
            _selectionGizmoRenderers.Box.Dispose();
            _selectionGizmoRenderers.Sphere.Dispose();
            _selectionGizmoRenderers.Cylinder.Dispose();
            _highlightRenderer.Dispose();
            _selection.Dispose();
        }
    }
}
