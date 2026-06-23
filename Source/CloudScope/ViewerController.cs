using System;
using System.Diagnostics;
using CloudScope.Labeling;
using CloudScope.Library;
using CloudScope.Loading;
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

        private readonly ViewportState[] _viewports;
        private int _activeViewportIndex;
        private ViewportLayoutKind _viewportLayout = ViewportLayoutKind.SinglePerspective;
        private ViewportLayoutKind _previousViewportLayout = ViewportLayoutKind.SinglePerspective;
        private ViewportViewKind _auxiliaryViewportView = ViewportViewKind.Top;
        private bool _forceAuxiliaryViewRefresh;
        private float _cloudRadius = 50f;
        private PointCloudDataset? _dataset;

        private bool _suppressEscapeClose;

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
            _viewports =
            [
                new ViewportState(ViewportKind.Perspective3D, new OrbitCamera(), new CameraInputController()),
                new ViewportState(ViewportKind.Top2D, new OrbitCamera(), new CameraInputController())
            ];

            foreach (ViewportState viewport in _viewports)
                viewport.Camera.SetDepthPicker(new ViewportDepthPicker(renderBackend.CreateDepthPicker(), () => viewport.Bounds, () => _height));
        }

        public void Load()
        {
            _frameLifecycle.InitializeFrameState();
            _pointRenderer.Initialize();
            _overlayRenderer.Initialize();
            UpdateViewportLayout();
        }

        public void LoadPointCloud(PointData[] pts, float cloudRadius = 50f)
        {
            _dataset = null;
            _cloudRadius = cloudRadius;
            _selection.LoadPointCloud(pts);
            foreach (ViewportState viewport in _viewports)
                FitViewport(viewport);
            _pointRenderer.Upload(new PointCloudRenderData(pts));
        }

        public void LoadPointCloud(PointCloudDataset dataset)
        {
            _dataset = dataset;
            _cloudRadius = dataset.Radius;
            LoadDatasetIntoSelection();
            foreach (ViewportState viewport in _viewports)
                FitViewport(viewport);
            UploadDatasetView();
        }

        // Feed the current dataset view into the selection controller, preserving the
        // visible→source index map so labels stay keyed by stable source indices.
        private void LoadDatasetIntoSelection()
        {
            if (_dataset == null) return;
            _selection.LoadPointCloud(_dataset.ViewPoints, _dataset.ViewToSource, _dataset.SourcePoints, _dataset.Attributes);
        }

        public string OpenPointCloud(string path, long maxPoints = 50_000_000)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "OPEN requires a file path.";

            if (!File.Exists(path))
                return $"File not found: {path}";

            var sw = Stopwatch.StartNew();
            using var reader = new LasReader(path);
            LoadedPointCloud cloud = PointCloudLoader.Load(reader, maxPoints);
            LoadPointCloud(cloud.ToDataset());
            SetLasFilePath(path);
            sw.Stop();

            return $"Loaded {cloud.LoadedCount:N0} points from {Path.GetFileName(path)} ({sw.Elapsed.TotalSeconds:0.0}s).";
        }

        public void SetLasFilePath(string path) => _selection.SetLasFilePath(path);

        public void Reset()
        {
            _cloudRadius = 50f;
            _dataset = null;
            _selection.Reset();
            foreach (ViewportState viewport in _viewports)
                FitViewport(viewport);
            _pointRenderer.Upload(PointCloudRenderData.Empty);
        }

        public bool NeedsContinuousFrames => _viewports.Any(v => v.Input.NeedsContinuousFrames);

        public InteractionMode Mode => _selection.Mode;
        public SelectionInteractionState SelectionInteractionState => _selection.InteractionState;
        public SelectionToolType ActiveToolType => _selection.ActiveTool.ToolType;
        public bool HasActiveSelection => _selection.HasActiveSelection;
        public double ZoomScale => ActiveViewport.Camera.Hvs;
        public float PointSize => ActiveViewport.Input.PointSize;

        public void SetMode(InteractionMode mode) => _selection.SetMode(mode);

        public void SetTool(SelectionToolType toolType) => _selection.SetTool(toolType);

        public void SetLabel(string label) => _selection.SetLabel(label);

        // ── Label registry (name → ASPRS class code + color) ──────────────────
        public LabelRegistry LabelRegistry => _selection.Registry;
        public string CurrentLabel => _selection.CurrentLabel;
        public int? CurrentInstanceId => _selection.CurrentInstanceId;
        public bool LabelWindowVisible { get; set; }
        public void ToggleLabelWindow() => LabelWindowVisible = !LabelWindowVisible;

        public string DefineLabel(string name, byte code)
        {
            LabelDefinition def = _selection.Registry.Define(name, code);
            _highlightRenderer.MarkDirty();
            return $"Label '{def.Name}' = class {def.Code} ({PointCloudAttributes.FormatClassName(def.Code)}).";
        }

        public string SetActiveLabel(string name)
        {
            _selection.SetLabel(name);
            return DescribeActiveLabel(name);
        }

        public string SetActiveAnnotation(string name, int? instanceId)
        {
            _selection.SetLabel(name);
            _selection.SetInstanceId(instanceId);
            string labelText = DescribeActiveLabel(name);
            return instanceId is int id ? $"{labelText} Instance: {id}." : $"{labelText} Instance: none.";
        }

        public string SetActiveInstance(int? instanceId)
        {
            _selection.SetInstanceId(instanceId);
            return instanceId is int id ? $"Active instance: {id}." : "Active instance cleared.";
        }

        private string DescribeActiveLabel(string name)
        {
            byte? code = _selection.Registry.CodeFor(name);
            string labelText = code is { } c
                ? $"Active label: '{name}' (class {c})."
                : $"Active label: '{name}' (no class code — define it with LABELDEF).";
            return _selection.CurrentInstanceId is int instanceId
                ? $"{labelText} Instance: {instanceId}."
                : labelText;
        }

        public void ConfirmActiveSelection() => _selection.ConfirmActiveSelection();

        public string FitActiveSelection(bool useGround) => _selection.FitActiveToolToSelection(useGround);

        public void CancelOrExitLabelMode() => _selection.CancelOrExitLabelMode();

        public bool UndoSelectionCommand() => _selection.UndoSelectionCommand();

        public void ZoomExtents() => FitViewport(ActiveViewport);

        public void ZoomByFactor(float factor)
        {
            ViewportState viewport = ActiveViewport;
            int cx = viewport.Bounds.Width / 2;
            int cy = viewport.Bounds.Height / 2;
            viewport.Camera.PickDepthWindow(cx, cy, 11);
            viewport.Camera.Zoom(cx, cy, factor);
        }

        public void ZoomWindow(int x0, int y0, int x1, int y1) => ActiveViewport.Camera.ZoomWindow(x0, y0, x1, y1);

        public void ZoomCenter()
        {
            ViewportState viewport = ActiveViewport;
            viewport.Camera.FocusOnCursor(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2);
        }

        public void SetPointSize(float size)
        {
            foreach (ViewportState viewport in _viewports)
                viewport.Input.SetPointSize(size);
        }

        public string DescribeAttributes(string attribute)
        {
            if (_dataset == null)
                return "No point cloud is loaded.";

            return NormalizeAttribute(attribute) switch
            {
                "" or "A" or "ALL" => _dataset.Attributes.DescribeAll(),
                "C" or "CLASS" => _dataset.Attributes.DescribeClass(),
                "I" or "INTENSITY" => _dataset.Attributes.DescribeIntensity(),
                "R" or "RETURN" => _dataset.Attributes.DescribeReturn(),
                "Z" or "HEIGHT" => _dataset.Attributes.DescribeZ(),
                _ => $"Unknown attribute: {attribute}"
            };
        }

        public string ApplyPointFilter(PointFilter? filter)
        {
            if (_dataset == null)
                return "No point cloud is loaded.";

            string result = _dataset.ApplyFilter(filter);
            LoadDatasetIntoSelection();
            UploadDatasetView();
            return result;
        }

        public string SetColorSource(ColorSource source)
        {
            if (_dataset == null)
                return "No point cloud is loaded.";

            ColorSource target = _dataset.NormalizeColorSource(source);
            if (_dataset.CurrentColorSource == target)
                return $"Color: {target.ToDisplayName()}.";

            if (_pointRenderer.CanUpdateColorSourceWithoutUpload)
            {
                string shaderResult = _dataset.SetColorSource(source, recolor: false);
                _pointRenderer.UpdateColorSource(_dataset.CurrentColorSource);
                return shaderResult;
            }

            string result = _dataset.SetColorSource(source);
            LoadDatasetIntoSelection();
            UploadDatasetView();
            return result;
        }

        public string ClearColorSource()
        {
            if (_dataset == null)
                return "No point cloud is loaded.";

            if (_dataset.CurrentColorSource == _dataset.DefaultColorSource)
                return $"Color: {_dataset.DefaultColorSource.ToDisplayName()}.";

            if (_pointRenderer.CanUpdateColorSourceWithoutUpload)
            {
                string shaderResult = _dataset.ClearColorSource(recolor: false);
                _pointRenderer.UpdateColorSource(_dataset.CurrentColorSource);
                return shaderResult;
            }

            string result = _dataset.ClearColorSource();
            LoadDatasetIntoSelection();
            UploadDatasetView();
            return result;
        }

        private void UploadDatasetView()
        {
            if (_dataset == null)
                return;

            _pointRenderer.Upload(new PointCloudRenderData(
                _dataset.ViewPoints,
                _dataset.RenderOrder,
                _dataset.Attributes,
                _dataset.ViewToSource,
                _dataset.SourcePoints,
                _dataset.CurrentColorSource));
        }

        private static string NormalizeAttribute(string attribute) => attribute.Trim().ToUpperInvariant();

        public void SetView(string view)
        {
            OrbitCamera camera = ActiveViewport.Camera;
            switch (view.ToUpperInvariant())
            {
                case "FRONT": camera.SetFrontView(); break;
                case "BACK": camera.SetBackView(); break;
                case "LEFT": camera.SetLeftView(); break;
                case "RIGHT": camera.SetRightView(); break;
                case "TOP": camera.SetTopView(); break;
                case "BOTTOM": camera.SetBottomView(); break;
                case "ISOMETRIC": camera.SetIsometric(); break;
                default: throw new ArgumentOutOfRangeException(nameof(view), view, "Unknown standard view.");
            }
        }

        public bool IsPerspective => ActiveViewport.Camera.IsPerspective;

        public string ToggleProjection()
        {
            ViewportState viewport = ActiveViewport;
            viewport.Camera.ToggleProjection(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2);
            return $"Projection: {(viewport.Camera.IsPerspective ? "Perspective" : "Parallel")}.";
        }

        /// <summary>Set the active viewport's projection explicitly (AutoCAD PERSPECTIVE 1/0).</summary>
        public string SetProjection(bool perspective)
        {
            ViewportState viewport = ActiveViewport;
            if (viewport.Camera.IsPerspective != perspective)
                viewport.Camera.ToggleProjection(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2);
            return $"Projection: {(perspective ? "Perspective" : "Parallel")}.";
        }

        public string SetViewportLayout(ViewportLayoutKind layout)
            => SetViewportLayout(layout, _auxiliaryViewportView);

        public string SetViewportLayout(ViewportLayoutKind layout, ViewportViewKind view)
        {
            if (layout == ViewportLayoutKind.Previous)
                layout = _previousViewportLayout;

            _auxiliaryViewportView = view;
            _forceAuxiliaryViewRefresh = true;
            if (layout != _viewportLayout)
            {
                _previousViewportLayout = _viewportLayout;
                _viewportLayout = layout;
            }

            UpdateViewportLayout();
            return $"Viewport layout: {DescribeViewportLayout(_viewportLayout)}. View: {DescribeViewportView(_auxiliaryViewportView)}.";
        }

        public bool SaveLabels() => _selection.SaveLabels();

        public string SaveLabelsToLas() => _selection.SaveLabelsToLas();

        public bool LoadLabels() => _selection.LoadLabels();

        public void ClearLabels() => _selection.ClearLabels();

        public bool UpdateFrame(float dt, IViewerKeyboard keyboard)
        {
            if (_suppressEscapeClose && !keyboard.IsKeyDown(ViewerKey.Escape))
                _suppressEscapeClose = false;

            bool shouldClose = false;
            foreach (ViewportState viewport in ActiveViewports())
            {
                bool viewportShouldClose = viewport.Input.UpdateFrame(
                    dt,
                    keyboard,
                    viewport.Camera,
                    _cloudRadius,
                    _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume,
                    viewport.Bounds.Width,
                    viewport.Bounds.Height,
                    !_suppressEscapeClose);
                shouldClose |= viewportShouldClose;
            }

            _selection.UpdatePreview(dt);
            return shouldClose;
        }

        public void MouseDown(ViewerMouseButton button, int mx, int my)
        {
            if (!TryActivateViewport(mx, my, out ViewportState viewport, out int localX, out int localY))
                return;

            OrbitCamera camera = viewport.Camera;
            camera.CancelTransition();
            _selection.SetViewConstraint(ConstraintFor(viewport));
            if (button == ViewerMouseButton.Left)
            {
                bool toolConsumed = _selection.MouseDownLeft(localX, localY, camera);
                viewport.Input.MouseDown(
                    button,
                    localX,
                    localY,
                    camera,
                    toolConsumed,
                    _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume,
                    _selection.ActiveTool.Center,
                    viewport.Kind == ViewportKind.Perspective3D);
            }
            if (button != ViewerMouseButton.Left)
                viewport.Input.MouseDown(button, localX, localY, camera, false, false, Vector3.Zero);
        }

        public void MouseUp(ViewerMouseButton button, int mx, int my)
        {
            if (!TryGetViewportLocal(mx, my, out ViewportState viewport, out int localX, out int localY))
                viewport = ActiveViewport;

            _selection.SetViewConstraint(ConstraintFor(viewport));
            if (button == ViewerMouseButton.Left) _selection.MouseUpLeft(localX, localY, viewport.Camera);
            viewport.Input.MouseUp(button);
        }

        public void MouseMove(int mx, int my)
        {
            if (!TryGetViewportLocal(mx, my, out ViewportState viewport, out int localX, out int localY))
                viewport = ActiveViewport;

            _selection.SetViewConstraint(ConstraintFor(viewport));
            _selection.MouseMove(localX, localY, viewport.Camera);
            viewport.Input.MouseMove(localX, localY, viewport.Camera);
        }

        public void MouseWheel(int mx, int my, float offsetY)
        {
            if (!TryActivateViewport(mx, my, out ViewportState viewport, out int localX, out int localY))
                return;

            if (_selection.MouseWheel(offsetY))
                return;

            viewport.Input.MouseWheel(localX, localY, offsetY, viewport.Camera, _cloudRadius);
        }

        public void KeyDown(ViewerKey key, bool ctrl, int mouseX, int mouseY)
        {
            if (_selection.KeyDown(key, ctrl) && key == ViewerKey.Escape)
                _suppressEscapeClose = true;
        }

        public void RenderFrame(double frameTime)
        {
            _frameTiming.BeginFrame();
            using var frame = _frameLifecycle.BeginFrame();
            var frameData = frame.FrameData;

            int totalDrawCount = 0;
            foreach (ViewportState viewport in ActiveViewports())
            {
                ApplyRenderViewport(viewport);
                var view = viewport.Camera.GetViewMatrix();
                var proj = viewport.Camera.GetProjectionMatrix();

                Breadcrumb("point-render");
                int drawCount = _pointRenderer.Render(frameData, ref view, ref proj, viewport.Input.PointSize, viewport.Camera.Hvs, _cloudRadius);
                totalDrawCount += drawCount;

                Breadcrumb("highlight");
                if (_selection.SourcePoints != null && _selection.Labels.Count > 0)
                    _highlightRenderer.Render(frameData, _selection.SourcePoints, _selection.Labels, _selection.ResolveAnnotationColor, ref view, ref proj, viewport.Input.PointSize);

                Breadcrumb("preview");
                if (_selection.TryTakePreview(out var previewIndices))
                    _highlightRenderer.UpdatePreview(_selection.Points, previewIndices);
                _highlightRenderer.RenderPreview(frameData, ref view, ref proj, viewport.Input.PointSize);

                if (_selection.Mode == InteractionMode.Label)
                {
                    var tool = _selection.ActiveTool;
                    tool.ViewConstraint = ConstraintFor(viewport);
                    Breadcrumb($"gizmo {tool.ToolType} phase={tool.Phase} vol={tool.HasVolume}");
                    if (!_selectionGizmoRenderers.TryRenderPlacement(frameData, tool, viewport.Bounds.Width, viewport.Bounds.Height)
                        && (tool.HasVolume || tool.Phase == ToolPhase.Drawing))
                    {
                        _selectionGizmoRenderers.Render(frameData, tool, view, proj, viewport.Camera);
                    }
                }

                bool anyToolActive = _selection.Mode == InteractionMode.Label && _selection.ActiveTool.HasVolume;
                if (!anyToolActive && (viewport.Input.PivotFade > 0.01f || viewport.Input.PivotFlash > 0.01f))
                    _overlayRenderer.RenderPivotIndicator(frameData, ref view, ref proj, viewport.Camera, viewport.Input.DisplayPivot, viewport.Input.PivotFade, viewport.Input.PivotFlash);

                float crossAlpha = 1f - viewport.Input.PivotFade;
                if (crossAlpha > 0.01f)
                    _overlayRenderer.RenderCenterCrosshair(frameData, viewport.Bounds.Width, viewport.Bounds.Height, crossAlpha);

                if (_selection.Mode == InteractionMode.Label)
                    _overlayRenderer.RenderModeIndicator(frameData, viewport.Bounds.Width, viewport.Bounds.Height, _selection.ActiveTool.ToolType);
            }

            int loadedCount = _dataset?.LoadedCount ?? _pointRenderer.PointCount;
            int visibleCount = _dataset?.VisibleCount ?? _pointRenderer.PointCount;
            _frameTiming.MarkMainDraw(totalDrawCount, loadedCount, visibleCount, _pointRenderer.PointCount);
            _frameTiming.MarkHighlight();
            _frameTiming.MarkPreview();
            _frameTiming.MarkGizmo();

            Breadcrumb("overlay-done");
            _frameTiming.MarkSwapAndLog(frameTime);
        }

        // ── Crash breadcrumb (temporary AV diagnostic) ────────────────────────
        // Writes the current render phase to %TEMP%\cloudscope_phase.log with an
        // immediate flush. Because an access violation faults synchronously on the
        // offending GL call, the LAST line in this file names the phase that crashed.
        private static readonly string BreadcrumbPath =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cloudscope_phase.log");
        private static long _breadcrumbSeq;

        private static void Breadcrumb(string phase)
        {
            try
            {
                // Overwrite so the file stays tiny; the last write = the phase that crashed.
                System.IO.File.WriteAllText(BreadcrumbPath,
                    $"seq {System.Threading.Interlocked.Increment(ref _breadcrumbSeq)} @ {DateTime.Now:HH:mm:ss.fff}{Environment.NewLine}phase: {phase}{Environment.NewLine}");
            }
            catch { /* diagnostics must never throw */ }
        }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            _frameLifecycle.Resize(width, height);
            UpdateViewportLayout();
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

        private ViewportState ActiveViewport => _viewports[_activeViewportIndex];

        // Grip view constraint for a viewport: only the orthographic auxiliary viewport
        // looking along a world axis (Top/Bottom, Left/Right, Front/Back) constrains grips.
        private GripViewConstraint ConstraintFor(ViewportState viewport)
        {
            if (viewport.Kind != ViewportKind.Top2D)
                return GripViewConstraint.None;

            return _auxiliaryViewportView switch
            {
                ViewportViewKind.Left or ViewportViewKind.Right => GripViewConstraint.AlongWorldAxis(0),
                ViewportViewKind.Front or ViewportViewKind.Back => GripViewConstraint.AlongWorldAxis(1),
                ViewportViewKind.Top or ViewportViewKind.Bottom => GripViewConstraint.AlongWorldAxis(2),
                _ => GripViewConstraint.None
            };
        }

        private IEnumerable<ViewportState> ActiveViewports()
        {
            return _viewportLayout switch
            {
                ViewportLayoutKind.TwoVertical or ViewportLayoutKind.TwoHorizontal => _viewports,
                ViewportLayoutKind.SingleTop => [_viewports[1]],
                _ => [_viewports[0]]
            };
        }

        private void UpdateViewportLayout()
        {
            int safeWidth = Math.Max(1, _width);
            int safeHeight = Math.Max(1, _height);

            switch (_viewportLayout)
            {
                case ViewportLayoutKind.SingleTop:
                    SetViewportBounds(_viewports[1], new ViewportBounds(0, 0, safeWidth, safeHeight));
                    _activeViewportIndex = 1;
                    break;
                case ViewportLayoutKind.TwoVertical:
                    int halfWidth = Math.Max(1, safeWidth / 2);
                    SetViewportBounds(_viewports[0], new ViewportBounds(0, 0, halfWidth, safeHeight));
                    SetViewportBounds(_viewports[1], new ViewportBounds(halfWidth, 0, safeWidth - halfWidth, safeHeight));
                    _activeViewportIndex = Math.Clamp(_activeViewportIndex, 0, 1);
                    break;
                case ViewportLayoutKind.TwoHorizontal:
                    int halfHeight = Math.Max(1, safeHeight / 2);
                    SetViewportBounds(_viewports[0], new ViewportBounds(0, 0, safeWidth, halfHeight));
                    SetViewportBounds(_viewports[1], new ViewportBounds(0, halfHeight, safeWidth, safeHeight - halfHeight));
                    _activeViewportIndex = Math.Clamp(_activeViewportIndex, 0, 1);
                    break;
                default:
                    SetViewportBounds(_viewports[0], new ViewportBounds(0, 0, safeWidth, safeHeight));
                    _activeViewportIndex = 0;
                    break;
            }

            foreach (ViewportState viewport in ActiveViewports())
            {
                if (viewport.Kind == ViewportKind.Top2D && (!viewport.IsInitialized || _forceAuxiliaryViewRefresh))
                    FitViewport(viewport);
                else
                    FitViewport(viewport, preserveZoom: true);

                viewport.IsInitialized = true;
            }

            _forceAuxiliaryViewRefresh = false;
        }

        private static void SetViewportBounds(ViewportState viewport, ViewportBounds bounds)
        {
            viewport.Bounds = bounds;
            viewport.Camera.SetViewportSize(bounds.Width, bounds.Height);
        }

        private void FitViewport(ViewportState viewport, bool preserveZoom = false)
        {
            if (preserveZoom)
                return;

            if (viewport.Kind == ViewportKind.Top2D)
            {
                viewport.Camera.SetOrthographicStandardView(DescribeViewportView(_auxiliaryViewportView), _cloudRadius);
                return;
            }

            viewport.Camera.FitToCloud(_cloudRadius);
        }

        private void ApplyRenderViewport(ViewportState viewport)
        {
            int y = Math.Max(0, _height - viewport.Bounds.Y - viewport.Bounds.Height);
            _frameLifecycle.SetViewport(viewport.Bounds.X, y, viewport.Bounds.Width, viewport.Bounds.Height);
        }

        private bool TryActivateViewport(int x, int y, out ViewportState viewport, out int localX, out int localY)
        {
            if (!TryGetViewportLocal(x, y, out viewport, out localX, out localY))
                return false;

            _activeViewportIndex = Array.IndexOf(_viewports, viewport);
            return true;
        }

        private bool TryGetViewportLocal(int x, int y, out ViewportState viewport, out int localX, out int localY)
        {
            foreach (ViewportState candidate in ActiveViewports())
            {
                if (!candidate.Bounds.Contains(x, y))
                    continue;

                viewport = candidate;
                localX = x - candidate.Bounds.X;
                localY = y - candidate.Bounds.Y;
                return true;
            }

            viewport = ActiveViewport;
            localX = Math.Clamp(x - viewport.Bounds.X, 0, Math.Max(0, viewport.Bounds.Width - 1));
            localY = Math.Clamp(y - viewport.Bounds.Y, 0, Math.Max(0, viewport.Bounds.Height - 1));
            return false;
        }

        private static string DescribeViewportLayout(ViewportLayoutKind layout) => layout switch
        {
            ViewportLayoutKind.SingleTop => "Single auxiliary",
            ViewportLayoutKind.TwoVertical => "Two vertical, Perspective + auxiliary",
            ViewportLayoutKind.TwoHorizontal => "Two horizontal, Perspective + auxiliary",
            _ => "Single perspective"
        };

        public static string DescribeViewportView(ViewportViewKind view) => view switch
        {
            ViewportViewKind.Front => "Front",
            ViewportViewKind.Back => "Back",
            ViewportViewKind.Left => "Left",
            ViewportViewKind.Right => "Right",
            ViewportViewKind.Bottom => "Bottom",
            ViewportViewKind.Isometric => "Isometric",
            _ => "Top"
        };

        private sealed class ViewportState(ViewportKind kind, OrbitCamera camera, CameraInputController input)
        {
            public ViewportKind Kind { get; } = kind;
            public OrbitCamera Camera { get; } = camera;
            public CameraInputController Input { get; } = input;
            public ViewportBounds Bounds { get; set; } = new(0, 0, 1, 1);
            public bool IsInitialized { get; set; }
        }

        private readonly record struct ViewportBounds(int X, int Y, int Width, int Height)
        {
            public bool Contains(int x, int y) =>
                x >= X && y >= Y && x < X + Width && y < Y + Height;
        }

        private sealed class ViewportDepthPicker(
            IDepthPicker inner,
            Func<ViewportBounds> getBounds,
            Func<int> getFramebufferHeight) : IDepthPicker
        {
            public float ReadDepth(int x, int y)
            {
                ViewportBounds bounds = getBounds();
                int globalY = getFramebufferHeight() - bounds.Y - bounds.Height + y;
                return inner.ReadDepth(bounds.X + x, globalY);
            }

            public int ReadDepthWindow(int x, int y, int width, int height, float[] destination)
            {
                ViewportBounds bounds = getBounds();
                int globalY = getFramebufferHeight() - bounds.Y - bounds.Height + y;
                return inner.ReadDepthWindow(bounds.X + x, globalY, width, height, destination);
            }
        }
    }

    public enum ViewportLayoutKind
    {
        SinglePerspective,
        SingleTop,
        TwoVertical,
        TwoHorizontal,
        Previous
    }

    public enum ViewportKind
    {
        Perspective3D,
        Top2D
    }

    public enum ViewportViewKind
    {
        Top,
        Bottom,
        Left,
        Right,
        Front,
        Back,
        Isometric
    }
}
