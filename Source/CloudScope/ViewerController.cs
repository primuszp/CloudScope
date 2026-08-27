using System;
using System.Diagnostics;
using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Library;
using CloudScope.Loading;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;
using CloudScope.Store;

namespace CloudScope
{
    public sealed class ViewerController : IDisposable
    {
        private readonly IRenderFrameLifecycle _frameLifecycle;
        private readonly IPointCloudRenderer _pointRenderer;
        private readonly IPointTileCloudRenderer _streamingRenderer;
        private readonly IOverlayRenderer _overlayRenderer;
        private readonly IHighlightRenderer _highlightRenderer;
        private readonly SelectionGizmoRenderers _selectionGizmoRenderers;
        private readonly FrameTimingDiagnostics _frameTiming = new();
        private readonly SelectionController _selection;

        private readonly ViewportState[] _viewports;
        private int _activeViewportIndex;
        private ViewportState? _pointerCaptureViewport;
        private ViewportLayoutKind _viewportLayout = ViewportLayoutKind.SinglePerspective;
        private ViewportLayoutKind _previousViewportLayout = ViewportLayoutKind.SinglePerspective;
        private ViewportViewKind _auxiliaryViewportView = ViewportViewKind.Top;
        private bool _forceAuxiliaryViewRefresh;
        private float _cloudRadius = 50f;
        private PointCloudDataset? _dataset;

        /// <summary>
        /// The on-disk clouds being streamed, empty when the resident path is drawing.
        /// </summary>
        /// <remarks>
        /// Streaming and the resident path are exclusive rather than layered: a store has no
        /// <c>PointData[]</c> behind it, so selection and labelling are unavailable while one
        /// is open, and opening a LAS the ordinary way closes them all. Several stores may be
        /// open at once, though — they share one page budget, so a second cloud does not halve
        /// the first one's detail, it competes for the same pages by how close it is.
        /// </remarks>
        private readonly List<PointTileLayer> _layers = [];
        private string _sourceName = "";
        private float _smoothedFps;

        private bool _suppressEscapeClose;

        private int _width;
        private int _height;
        private ViewportInset _inset;

        public ViewerController(int width, int height, IRenderBackend renderBackend)
        {
            _width = width;
            _height = height;
            _frameLifecycle = renderBackend;
            _pointRenderer = renderBackend.CreatePointCloudRenderer();
            _streamingRenderer = renderBackend.CreateStreamingPointCloudRenderer();
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

            _backendName = renderBackend.Kind.ToString();
            RegisterSystemVariables();

            // One undo step per labelling command: the label manager reports each reversible
            // change, and the runtime's command marks decide where the steps begin and end.
            _selection.Labels.ActionRecorded += () => UndoHistory.Record(
                new DelegateUndoAction("Label change",
                    () => _selection.Labels.Undo(),
                    () => _selection.Labels.Redo()));
        }

        private readonly string _backendName;

        /// <summary>Rendering backend this viewer was created with.</summary>
        public string RenderBackendName => _backendName;

        /// <summary>
        /// The command surface driving this viewer, set by the dispatcher. It is consulted
        /// before a click becomes a selection gesture: while a command is waiting for a point,
        /// clicking in the viewport answers that prompt.
        /// </summary>
        public ICommandExecutor? CommandPrompts { get; set; }

        /// <summary>Command-level undo history; the runtime opens and closes its marks.</summary>
        public UndoManager UndoHistory { get; } = new();

        /// <summary>Named viewer state, readable and writable with SETVAR and GETVAR.</summary>
        public SystemVariableTable Variables { get; } = new();

        /// <summary>Set by QUIT; the host closes the window on the next frame.</summary>
        public bool CloseRequested { get; private set; }

        public void RequestClose() => CloseRequested = true;

        /// <summary>Frame timing and point throughput, for TIME.</summary>
        public string DescribeFrameTiming()
        {
            ViewerStatusSnapshot status = Status;
            return $"Frame rate    {status.Fps:0.0} fps"
                 + Environment.NewLine + $"Backend       {_backendName}"
                 + Environment.NewLine + $"Points loaded {status.LoadedCount:N0}"
                 + Environment.NewLine + $"Points drawn  {status.VisibleCount:N0}"
                 + Environment.NewLine + $"Frame budget  {(PointRenderBudget.MaxDrawPointsPerFrame?.ToString("N0") ?? PointRenderLimits.DefaultFrameBudget.ToString("N0"))} points"
                 + Environment.NewLine + $"Verbose log   {(_frameTiming.Enabled ? "on (CLOUDSCOPE_FRAME_LOG)" : "off")}";
        }

        private void RegisterSystemVariables()
        {
            Variables.RegisterReal("PDSIZE", "On-screen size of a point, in pixels.",
                () => ActiveViewport.Input.PointSize, value => SetPointSize((float)value), 0.5, 20);

            Variables.RegisterBool("PERSPECTIVE", "Perspective projection (1) or parallel (0).",
                () => ActiveViewport.Camera.IsPerspective, value => SetProjection(value));

            Variables.RegisterEnum("COLORSOURCE", "Attribute the cloud is coloured by.",
                ["Rgb", "Height", "Class", "Intensity", "Return"],
                () => (_dataset?.CurrentColorSource ?? ColorSource.Rgb).ToString(),
                value => SetColorSource(Enum.Parse<ColorSource>(value, ignoreCase: true)));

            Variables.RegisterString("VPORTLAYOUT", "Current viewport layout.",
                () => DescribeViewportLayout(_viewportLayout));

            Variables.RegisterString("VIEWNAME", "Name of the current view.", () => Status.ViewName);

            Variables.RegisterString("SELMODE", "Navigate or Label.", () => _selection.Mode.ToString());

            Variables.RegisterString("SELTOOL", "Active selection tool.", () => _selection.ActiveTool.ToolType.ToString());

            Variables.RegisterString("CLABEL", "Label applied to new selections.",
                () => _selection.CurrentLabel, value => SetActiveLabel(value));

            Variables.RegisterString("CINSTANCE", "Instance id applied to new selections.",
                () => Status.InstanceText,
                value => SetActiveInstance(int.TryParse(value, out int id) ? id : null));

            Variables.RegisterInt("PTMAX", "Maximum points drawn per frame.",
                () => PointRenderBudget.MaxDrawPointsPerFrame ?? PointRenderLimits.DefaultFrameBudget,
                value => PointRenderBudget.MaxDrawPointsPerFrame = value,
                min: 10_000);

            Variables.RegisterInt("PTRESIDENT", "Maximum points kept on the GPU; applies to clouds opened after it changes.",
                () => PointRenderBudget.MaxResidentPoints ?? 0,
                value => PointRenderBudget.MaxResidentPoints = value > 0 ? value : null,
                min: 0);

            Variables.RegisterString("RENDERBACKEND", "Rendering backend in use (read-only for this session).",
                () => _backendName);

            Variables.RegisterString("SOURCENAME", "Name of the open cloud.", () => _sourceName);
            Variables.RegisterInt("LOADEDPOINTS", "Points in the open cloud.", () => Status.LoadedCount);
            Variables.RegisterInt("VISIBLEPOINTS", "Points passing the current filter.", () => Status.VisibleCount);
            Variables.RegisterReal("FPS", "Smoothed frame rate.", () => Status.Fps);
            Variables.RegisterInt("LABELCOUNT", "Number of labelled points.", () => _selection.Labels.Count);
        }

        public void Load()
        {
            _frameLifecycle.Initialize();
            _pointRenderer.Initialize();
            _streamingRenderer.Initialize();
            _overlayRenderer.Initialize();
            UpdateViewportLayout();

            // Opening stores from the environment, the same way the render limits and the
            // backend choice are set. It is how a large cloud is put on screen without typing
            // a path into the command window every run; several, separated the way the
            // platform separates paths, come up as layers of one scene.
            string? startupStores = Environment.GetEnvironmentVariable("CLOUDSCOPE_OPEN_STORE");
            if (string.IsNullOrWhiteSpace(startupStores))
                return;

            bool first = true;
            foreach (string path in startupStores.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Console.WriteLine(OpenPointTileStore(path, replace: first));
                first = false;
            }
        }

        public void LoadPointCloud(PointData[] pts, float cloudRadius = 50f)
        {
            CloseLayers();
            _dataset = null;
            _cloudRadius = cloudRadius;
            _selection.LoadPointCloud(pts);
            foreach (ViewportState viewport in _viewports)
                FitViewport(viewport);
            _pointRenderer.Upload(new PointCloudRenderData(pts));
        }

        public void LoadPointCloud(PointCloudDataset dataset)
        {
            CloseLayers();
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

        /// <summary>
        /// Work handed back from a background thread, run on the next frame. A cloud is read
        /// off the UI thread but uploaded on the one that owns the graphics context.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pendingWork = new();

        /// <summary>
        /// Output produced without a command being submitted — a load finishing. Shells route
        /// it to the command line, so a background result is reported where a typed one is.
        /// </summary>
        public event Action<string>? BackgroundMessage;

        /// <summary>Percentage of a load in progress, or -1 when nothing is loading.</summary>
        public int LoadProgress { get; private set; } = -1;

        /// <summary>
        /// Reads a LAS or LAZ file and shows it. The read happens on a background thread and
        /// the upload on the next frame, so the command returns at once and no shell has to
        /// own a loading path of its own to keep its window responsive.
        /// </summary>
        public string OpenPointCloud(string path, long maxPoints = 0)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "OPEN requires a file path.";

            if (!File.Exists(path))
                return $"File not found: {path}";

            if (LoadProgress >= 0)
                return "A point cloud is already loading.";

            LoadProgress = 0;
            string name = Path.GetFileName(path);

            _ = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var progress = new Progress<int>(percent => LoadProgress = percent);
                    using var reader = new LasReader(path);
                    LoadedPointCloud cloud = PointCloudLoader.Load(reader, maxPoints, progress: progress);
                    PointCloudDataset dataset = cloud.ToDataset();
                    sw.Stop();

                    _pendingWork.Enqueue(() =>
                    {
                        LoadPointCloud(dataset);
                        SetLasFilePath(path);
                        LoadProgress = -1;
                        BackgroundMessage?.Invoke(
                            $"Loaded {cloud.LoadedCount:N0} points from {name} ({sw.Elapsed.TotalSeconds:0.0}s).");
                    });
                }
                catch (Exception ex)
                {
                    _pendingWork.Enqueue(() =>
                    {
                        LoadProgress = -1;
                        BackgroundMessage?.Invoke($"Load failed: {ex.Message}");
                    });
                }
            });

            return $"Reading {name}...";
        }

        /// <summary>
        /// Opens an indexed cloud and draws it straight off disk, whatever its size.
        /// </summary>
        /// <remarks>
        /// Nothing here scales with the file: the cell table is read into memory, the points
        /// stay on disk, and the renderer pulls in only the cells the camera is looking at.
        /// The same call drives either backend — the streaming renderer came from the render
        /// backend, so Metal and OpenGL differ only in how a page reaches the GPU.
        /// </remarks>
        public string OpenPointTileStore(string directory) => OpenPointTileStore(directory, replace: true);

        /// <summary>
        /// Opens an indexed cloud and draws it straight off disk, whatever its size.
        /// </summary>
        /// <param name="replace">
        /// Whether the open store replaces what is on screen or joins it as another layer.
        /// </param>
        /// <remarks>
        /// Nothing here scales with the file: the cell table is read into memory, the points
        /// stay on disk, and the renderer pulls in only the cells the camera is looking at.
        /// The same call drives either backend — the streaming renderer came from the render
        /// backend, so Metal and OpenGL differ only in how a page reaches the GPU.
        /// </remarks>
        public string OpenPointTileStore(string directory, bool replace)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return "OPENSTORE requires a store directory.";

            if (!File.Exists(Path.Combine(directory, PointTileStore.IndexFileName)))
                return $"Not a point tile store: {directory}";

            var sw = Stopwatch.StartNew();
            PointTileStore store = PointTileStore.Open(directory);
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));

            if (replace)
            {
                // The resident path owns the frame while it holds points, so it is emptied.
                CloseLayers();
                _pointRenderer.Upload(PointCloudRenderData.Empty);
                _dataset = null;
                _selection.Reset();
            }
            else
            {
                // Adding a layer resizes the working set, so the renderer lets go of the old
                // one before it is handed the new list.
                _streamingRenderer.Close();
            }

            var layer = new PointTileLayer(name, store)
            {
                // Layers past the first are tinted apart by default, since two surveys of the
                // same site are otherwise impossible to tell from each other.
                Tint = _layers.Count == 0 ? Vector3.One : LayerTintPalette[_layers.Count % LayerTintPalette.Length]
            };
            _layers.Add(layer);

            _sourceName = _layers.Count == 1 ? name : $"{_layers.Count} layers";
            _cloudRadius = LayerRadius();
            _streamingRenderer.Open(_layers);
            // Selection resolves against the cell trees now, so labelling keeps working on a
            // cloud that never fits in memory.
            _selection.SetStreamedSource(_layers);
            if (replace)
            {
                foreach (ViewportState viewport in _viewports)
                    FitViewport(viewport);
            }

            sw.Stop();
            return $"Streaming {store.Header.PointCount:N0} points from {name} "
                + $"({store.Header.NodeCount:N0} cells, {sw.Elapsed.TotalSeconds:0.0}s).";
        }

        /// <summary>
        /// Default tints for layers past the first, kept far apart in hue so overlapping
        /// clouds separate at a glance.
        /// </summary>
        private static readonly Vector3[] LayerTintPalette =
        [
            new(1.0f, 1.0f, 1.0f),
            new(1.0f, 0.6f, 0.5f),
            new(0.5f, 1.0f, 0.7f),
            new(0.6f, 0.7f, 1.0f),
            new(1.0f, 0.95f, 0.5f)
        ];

        /// <summary>Every open layer, in the order they were opened.</summary>
        public IReadOnlyList<PointTileLayer> Layers => _layers;

        /// <summary>Shows or hides one layer by name, and reports what happened.</summary>
        public string SetLayerVisible(string name, bool visible)
        {
            PointTileLayer? layer = FindLayer(name);
            if (layer is null)
                return $"No such layer: {name}";

            // Visibility costs nothing to change: the renderer holds the list live, and a
            // hidden layer simply stops being traversed, so its pages age out on their own.
            layer.Visible = visible;
            return $"Layer {layer.Name} {(visible ? "shown" : "hidden")}.";
        }

        /// <summary>Closes one layer by name, or every layer when the name is empty.</summary>
        /// <summary>Structure of the open stores, for STOREINFO.</summary>
        public string DescribeStores()
        {
            if (_layers.Count == 0)
                return "No point tile store is open. OPENSTORE loads one.";

            return string.Join(Environment.NewLine, _layers.Select(layer =>
            {
                Store.PointTileStoreHeader header = layer.Store.Header;
                return $"  {layer.Name}"
                     + Environment.NewLine + $"    directory   {layer.Store.Directory}"
                     + Environment.NewLine + $"    points      {header.PointCount:N0}"
                     + Environment.NewLine + $"    cells       {layer.Store.Nodes.Length:N0}"
                     + Environment.NewLine + $"    source col  {(layer.Store.HasSourceIndices ? "yes" : "no")}"
                     + Environment.NewLine + $"    source file {layer.Store.SourcePath ?? "(none)"}";
            }));
        }

        public string CloseLayer(string name)
        {
            if (name.Length == 0)
            {
                int closed = _layers.Count;
                CloseLayers();
                return closed == 0 ? "No layers are open." : $"Closed {closed} layer(s).";
            }

            PointTileLayer? layer = FindLayer(name);
            if (layer is null)
                return $"No such layer: {name}";

            _streamingRenderer.Close();
            _layers.Remove(layer);
            layer.Store.Dispose();

            if (_layers.Count > 0)
            {
                _cloudRadius = LayerRadius();
                _streamingRenderer.Open(_layers);
            }

            _selection.SetStreamedSource(_layers);

            _sourceName = _layers.Count switch
            {
                0 => "",
                1 => _layers[0].Name,
                _ => $"{_layers.Count} layers"
            };
            return $"Closed layer {layer.Name}.";
        }

        private PointTileLayer? FindLayer(string name) =>
            _layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The framing radius for every open layer taken together.</summary>
        private float LayerRadius()
        {
            if (_layers.Count == 0)
                return 50f;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (PointTileLayer layer in _layers)
            {
                PointTileStoreHeader header = layer.Store.Header;
                // Bounds are absolute, but the points are stored relative to each store's own
                // origin, so a layer's extent has to be measured where it will be drawn.
                minX = Math.Min(minX, header.MinX - header.OriginX);
                minY = Math.Min(minY, header.MinY - header.OriginY);
                minZ = Math.Min(minZ, header.MinZ - header.OriginZ);
                maxX = Math.Max(maxX, header.MaxX - header.OriginX);
                maxY = Math.Max(maxY, header.MaxY - header.OriginY);
                maxZ = Math.Max(maxZ, header.MaxZ - header.OriginZ);
            }

            float x = (float)(maxX - minX) * 0.5f;
            float y = (float)(maxY - minY) * 0.5f;
            float z = (float)(maxZ - minZ) * 0.5f;
            float radius = MathF.Sqrt(x * x + y * y + z * z);
            return radius > 0f ? radius : 50f;
        }

        private void CloseLayers()
        {
            if (_layers.Count == 0)
                return;

            // The renderer holds pages against the stores' mapped files, so it lets go first.
            _streamingRenderer.Close();
            _selection.SetStreamedSource(null);
            foreach (PointTileLayer layer in _layers)
                layer.Store.Dispose();
            _layers.Clear();
        }

        public void SetLasFilePath(string path)
        {
            _sourceName = Path.GetFileName(path);
            _selection.SetLasFilePath(path);
        }

        /// <summary>
        /// Current viewer state for status bars and properties panels. Cheap to build; the
        /// shells read it after each command and (for the panel-rendered UI) per frame.
        /// </summary>
        public ViewerStatusSnapshot Status => new()
        {
            SourceName = _sourceName,
            LoadedCount = StoredPointCount ?? _dataset?.LoadedCount ?? _pointRenderer.PointCount,
            VisibleCount = StoredPointCount ?? _dataset?.VisibleCount ?? _pointRenderer.PointCount,
            Filter = _dataset is { FilterDescription: not "None" } filtered ? filtered.FilterDescription : "",
            Mode = _selection.Mode,
            ActiveTool = _selection.ActiveTool.ToolType,
            InteractionState = _selection.InteractionState,
            HasActiveSelection = _selection.HasActiveSelection,
            CurrentLabel = _selection.CurrentLabel,
            CurrentInstanceId = _selection.CurrentInstanceId,
            ColorSource = _dataset?.CurrentColorSource ?? ColorSource.Rgb,
            PointSize = ActiveViewport.Input.PointSize,
            IsPerspective = ActiveViewport.Camera.IsPerspective,
            ViewportLayout = DescribeViewportLayout(_viewportLayout),
            ViewName = ActiveViewport.Camera.IsPerspective && _viewportLayout == ViewportLayoutKind.SinglePerspective
                ? "Perspective"
                : DescribeViewportView(_auxiliaryViewportView),
            Fps = _smoothedFps,
            LabelWindowVisible = LabelWindowVisible,
            CommandHistoryVisible = CommandHistoryVisible,
            CommandLineVisible = CommandLineVisible,
            CommandLineFloating = CommandLineFloating,
            CloseRequested = CloseRequested,
            LoadProgress = LoadProgress
        };

        /// <summary>
        /// Points in the open store, clamped to what a count field can hold.
        /// </summary>
        /// <remarks>
        /// The counts the shells display are <see cref="int"/>, which a two-billion-point
        /// cloud overflows. Widening them is part of the same change that gives selection
        /// 64-bit point indices; until then the status bar saturates rather than wrapping to a
        /// negative number.
        /// </remarks>
        private int? StoredPointCount => _layers.Count == 0
            ? null
            : (int)Math.Min(_layers.Sum(layer => layer.Store.Header.PointCount), int.MaxValue);

        public void Reset()
        {
            CloseLayers();
            _cloudRadius = 50f;
            _dataset = null;
            _sourceName = "";
            _selection.Reset();
            foreach (ViewportState viewport in _viewports)
                FitViewport(viewport);
            _pointRenderer.Upload(PointCloudRenderData.Empty);
        }

        /// <summary>
        /// Whether the shell should keep drawing without waiting for input.
        /// </summary>
        /// <remarks>
        /// A streaming cloud loads across frames, so a still camera has to keep drawing until
        /// the cells it asked for have arrived — otherwise a shell that renders on demand
        /// would leave the picture half loaded until the user happened to move.
        /// </remarks>
        public bool NeedsContinuousFrames =>
            _viewports.Any(v => v.Input.NeedsContinuousFrames) || _streamingRenderer.PendingCellCount > 0;

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
        public int LabelledPointCount => _selection.Labels.Count;
        public int? CurrentInstanceId => _selection.CurrentInstanceId;
        public bool LabelWindowVisible { get; set; }

        /// <summary>Whether the expanded command-history panel (F2) is shown.</summary>
        public bool CommandHistoryVisible { get; set; }

        /// <summary>Whether the docked command window is shown (Ctrl+9).</summary>
        public bool CommandLineVisible { get; set; } = true;

        /// <summary>Whether the command window floats free of the workspace.</summary>
        public bool CommandLineFloating { get; set; }
        public void ToggleLabelWindow() => LabelWindowVisible = !LabelWindowVisible;

        public string DefineLabel(string name, byte code, Vector3? color = null)
        {
            LabelDefinition def = _selection.Registry.Define(name, code, color);
            _highlightRenderer.MarkDirty();
            string colorText = color is Vector3 c ? $"  colour {c.X:0.##},{c.Y:0.##},{c.Z:0.##}" : "";
            return $"Label '{def.Name}' = class {def.Code} ({PointCloudAttributes.FormatClassName(def.Code)}).{colorText}";
        }

        public string RemoveLabelDefinition(string name)
        {
            bool removed = _selection.Registry.Remove(name);
            if (removed) _highlightRenderer.MarkDirty();
            return removed ? $"Label definition '{name}' removed." : $"No label definition named '{name}'.";
        }

        public string DescribeLabelDefinitions()
        {
            IReadOnlyCollection<LabelDefinition> definitions = _selection.Registry.Definitions;
            if (definitions.Count == 0)
                return "No label definitions. Add one with LABELDEF.";

            return string.Join(Environment.NewLine, definitions
                .OrderBy(d => d.Code)
                .Select(d => $"  {d.Name}  class {d.Code} ({PointCloudAttributes.FormatClassName(d.Code)})"
                           + $"  colour {d.Color.X:0.##},{d.Color.Y:0.##},{d.Color.Z:0.##}"));
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

        public string MoveActiveSelection(Vector3 displacement) => _selection.MoveActiveSelection(displacement);
        public string RotateActiveSelection(float degrees, int axis) => _selection.RotateActiveSelection(degrees, axis);
        public string ScaleActiveSelection(float factor) => _selection.ScaleActiveSelection(factor);

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

        public string PanByPixels(int dx, int dy)
        {
            ActiveViewport.Camera.PanPixels(dx, dy);
            return $"Panned {dx},{dy} pixels.";
        }

        public string OrbitBy(float azimuthDegrees, float elevationDegrees)
        {
            ActiveViewport.Camera.OrbitBy(azimuthDegrees, elevationDegrees);
            return $"Orbited {azimuthDegrees:0.###}°, {elevationDegrees:0.###}°.";
        }

        public string SetOrbitPivot(Vector3 worldPoint)
        {
            ActiveViewport.Camera.SetOrbitPivot(worldPoint);
            return $"Pivot: {worldPoint.X:0.###},{worldPoint.Y:0.###},{worldPoint.Z:0.###}";
        }

        public string SetOrbitPivotFromScreen(int x, int y)
        {
            OrbitCamera camera = ActiveViewport.Camera;
            if (!camera.SetOrbitPivotFromScreen(x, y))
                return "No point under that position.";

            Vector3 pivot = camera.Pivot;
            return $"Pivot: {pivot.X:0.###},{pivot.Y:0.###},{pivot.Z:0.###}";
        }

        public string ResetOrbitPivot()
        {
            ActiveViewport.Camera.SetOrbitPivot(Vector3.Zero);
            return "Pivot reset to the cloud origin.";
        }

        // ----- Named views -----

        private readonly Dictionary<string, OrbitCamera.CameraState> _namedViews = new(StringComparer.OrdinalIgnoreCase);

        public string SaveNamedView(string name)
        {
            _namedViews[name] = ActiveViewport.Camera.SaveState();
            return $"View '{name}' saved.";
        }

        public string RestoreNamedView(string name)
        {
            if (!_namedViews.TryGetValue(name, out OrbitCamera.CameraState state))
                return $"No saved view named '{name}'.";

            ActiveViewport.Camera.RestoreState(state);
            return $"View '{name}' restored.";
        }

        public string DeleteNamedView(string name) =>
            _namedViews.Remove(name) ? $"View '{name}' deleted." : $"No saved view named '{name}'.";

        public string DescribeNamedViews() =>
            _namedViews.Count == 0
                ? "No saved views. Store one with VIEW Save."
                : "Saved views: " + string.Join(", ", _namedViews.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

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

            string result = _dataset.ApplyFilter(filter, recolor: !_pointRenderer.SupportsAttributeColoring);
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

        public bool SaveLabels(string? destinationPath = null) => _selection.SaveLabels(destinationPath);

        public string SaveLabelsToLas() => _selection.SaveLabelsToLas();

        public bool LoadLabels(string? sourcePath = null) => _selection.LoadLabels(sourcePath);

        public void ClearLabels() => _selection.ClearLabels();
        public string RemoveSelectionLabels() => _selection.RemoveActiveSelectionLabels();
        public string DescribeLabelStatistics() => _selection.DescribeLabelStatistics();

        public bool UpdateFrame(float dt, IViewerKeyboard keyboard)
        {
            while (_pendingWork.TryDequeue(out Action? work))
                work();

            if (dt > 0f)
                _smoothedFps = _smoothedFps <= 0f ? 1f / dt : _smoothedFps * 0.9f + 0.1f / dt;

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
            return shouldClose || CloseRequested;
        }

        public void MouseDown(ViewerMouseButton button, int mx, int my)
        {
            if (!TryActivateViewport(mx, my, out ViewportState viewport, out int localX, out int localY))
                return;

            OrbitCamera camera = viewport.Camera;
            camera.CancelTransition();

            // A pending point prompt owns the click. Answering it here rather than in every
            // host is what lets MOVE, PIVOT and ZOOM Window be driven by the mouse without
            // any of them growing a second, gesture-shaped implementation.
            if (button == ViewerMouseButton.Left && CommandPrompts?.AwaitsPoint == true)
            {
                camera.TryPickWorldPoint(localX, localY, 11, out Vector3 picked);
                CommandPrompts.SupplyPoint(picked, localX, localY);
                return;
            }

            _pointerCaptureViewport = viewport;
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
                    allowRotate: true);
            }
            if (button != ViewerMouseButton.Left)
                viewport.Input.MouseDown(button, localX, localY, camera, false, false, Vector3.Zero);
        }

        public void MouseUp(ViewerMouseButton button, int mx, int my)
        {
            ViewportState viewport = _pointerCaptureViewport ?? ActiveViewport;
            GetViewportLocal(viewport, mx, my, out int localX, out int localY);

            _selection.SetViewConstraint(ConstraintFor(viewport));
            if (button == ViewerMouseButton.Left) _selection.MouseUpLeft(localX, localY, viewport.Camera);
            viewport.Input.MouseUp(button);
            _pointerCaptureViewport = null;
        }

        public void MouseMove(int mx, int my)
        {
            ViewportState viewport;
            int localX, localY;
            if (_pointerCaptureViewport != null)
            {
                viewport = _pointerCaptureViewport;
                GetViewportLocal(viewport, mx, my, out localX, out localY);
            }
            else if (!TryGetViewportLocal(mx, my, out viewport, out localX, out localY))
            {
                viewport = ActiveViewport;
                GetViewportLocal(viewport, mx, my, out localX, out localY);
            }

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
                var renderView = new PointRenderView(
                    view, proj,
                    viewport.Bounds.Width, viewport.Bounds.Height,
                    viewport.Input.PointSize);
                int drawCount = _layers.Count == 0
                    ? _pointRenderer.Render(frameData, in renderView)
                    : _streamingRenderer.Render(frameData, in renderView);
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

            int loadedCount = StoredPointCount ?? _dataset?.LoadedCount ?? _pointRenderer.PointCount;
            int visibleCount = StoredPointCount ?? _dataset?.VisibleCount ?? _pointRenderer.PointCount;
            _frameTiming.MarkMainDraw(
                totalDrawCount, loadedCount, visibleCount, StoredPointCount ?? _pointRenderer.PointCount);
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

        /// <summary>
        /// Reserves edges of the framebuffer for a shell's panels, so the 3D view occupies
        /// only the rectangle the user can actually see. Everything derived from the viewport
        /// — the centre crosshair, zoom-to-centre, picking and the projection aspect — then
        /// refers to the visible area instead of the whole window.
        /// </summary>
        public void SetViewportInset(int left, int top, int right, int bottom)
        {
            var inset = new ViewportInset(
                Math.Max(0, left), Math.Max(0, top), Math.Max(0, right), Math.Max(0, bottom));

            if (inset == _inset)
                return;

            _inset = inset;
            UpdateViewportLayout();
        }

        public void Dispose()
        {
            CloseLayers();
            _streamingRenderer.Dispose();
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
            int left = _inset.Left;
            int top = _inset.Top;
            int safeWidth = Math.Max(1, _width - _inset.Left - _inset.Right);
            int safeHeight = Math.Max(1, _height - _inset.Top - _inset.Bottom);

            switch (_viewportLayout)
            {
                case ViewportLayoutKind.SingleTop:
                    SetViewportBounds(_viewports[1], new ViewportBounds(left, top, safeWidth, safeHeight));
                    _activeViewportIndex = 1;
                    break;
                case ViewportLayoutKind.TwoVertical:
                    int halfWidth = Math.Max(1, safeWidth / 2);
                    SetViewportBounds(_viewports[0], new ViewportBounds(left, top, halfWidth, safeHeight));
                    SetViewportBounds(_viewports[1], new ViewportBounds(left + halfWidth, top, safeWidth - halfWidth, safeHeight));
                    _activeViewportIndex = Math.Clamp(_activeViewportIndex, 0, 1);
                    break;
                case ViewportLayoutKind.TwoHorizontal:
                    int halfHeight = Math.Max(1, safeHeight / 2);
                    SetViewportBounds(_viewports[0], new ViewportBounds(left, top, safeWidth, halfHeight));
                    SetViewportBounds(_viewports[1], new ViewportBounds(left, top + halfHeight, safeWidth, safeHeight - halfHeight));
                    _activeViewportIndex = Math.Clamp(_activeViewportIndex, 0, 1);
                    break;
                default:
                    SetViewportBounds(_viewports[0], new ViewportBounds(left, top, safeWidth, safeHeight));
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

        private static void GetViewportLocal(ViewportState viewport, int x, int y, out int localX, out int localY)
        {
            localX = Math.Clamp(x - viewport.Bounds.X, 0, Math.Max(0, viewport.Bounds.Width - 1));
            localY = Math.Clamp(y - viewport.Bounds.Y, 0, Math.Max(0, viewport.Bounds.Height - 1));
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

        /// <summary>Framebuffer edges reserved for shell panels, in physical pixels.</summary>
        private readonly record struct ViewportInset(int Left, int Top, int Right, int Bottom);

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
