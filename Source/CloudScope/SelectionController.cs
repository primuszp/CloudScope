using CloudScope.Labeling;
using CloudScope.Library.Enums;
using CloudScope.Loading;
using CloudScope.Rendering;
using CloudScope.Selection;
using OpenTK.Mathematics;
using System.Linq;

namespace CloudScope
{
    public sealed class SelectionController : IDisposable
    {
        private const float PreviewInterval = 0.08f;

        private readonly IReadOnlyDictionary<SelectionToolType, ISelectionTool> _tools;
        private readonly SelectionPreviewWorker _previewWorker = new();
        private readonly LabelManager          _labelManager  = new();
        private readonly LabelRegistry         _registry      = new();
        private readonly Action<string>        _log;

        private SelectionToolType        _activeToolType = SelectionToolType.Box;
        private EditAction               _pendingAction    = EditAction.None;
        private SelectionInteractionState _interactionState = SelectionInteractionState.Navigate;
        private float  _previewTimer    = 999f;
        private bool   _previewWasActive;
        private string _currentLabel   = "Ground";
        private PointData[]? _points;            // visible (ViewPoints) — selection geometry + preview
        private PointData[]? _sourcePoints;      // full source-order array — highlight + fitting
        private int[]? _viewToSource;            // visible index → source index
        private PointCloudAttributes? _attributes;
        private string _lasFilePath    = "";

        public SelectionController(Action labelsChanged, Action<string>? log = null)
        {
            ISelectionTool[] tools =
            [
                new BoxSelectionTool(),
                new SphereSelectionTool(),
                new CylinderSelectionTool()
            ];
            _tools = tools.ToDictionary(tool => tool.ToolType);
            _labelManager.LabelsChanged += labelsChanged;
            _log = log ?? Console.WriteLine;
        }

        public InteractionMode Mode { get; private set; } = InteractionMode.Navigate;
        public SelectionInteractionState InteractionState => _interactionState;
        public ISelectionTool ActiveTool => _tools[_activeToolType];
        public LabelManager Labels => _labelManager;
        public LabelRegistry Registry => _registry;
        public string CurrentLabel => _currentLabel;

        /// <summary>Display color for a label: the registry's code color, or a hashed fallback.</summary>
        public Vector3 ResolveLabelColor(string name) => _registry.ColorFor(name) ?? LabelColorPalette.GetColor(name);

        public PointData[]? Points => _points;
        /// <summary>Full source-order points; highlights are keyed by source index.</summary>
        public PointData[]? SourcePoints => _sourcePoints;
        public PointCloudAttributes? Attributes => _attributes;
        public bool HasActiveSelection => ActiveTool.IsActive || ActiveTool.IsEditing;

        public void LoadPointCloud(PointData[] points)
            => LoadPointCloud(points, null, points, null);

        public void LoadPointCloud(
            PointData[] viewPoints,
            int[]? viewToSource,
            PointData[]? sourcePoints,
            PointCloudAttributes? attributes)
        {
            _points = viewPoints;
            _viewToSource = viewToSource;
            _sourcePoints = sourcePoints ?? viewPoints;
            _attributes = attributes;
        }

        public void SetLasFilePath(string path) => _lasFilePath = path;

        public void Reset()
        {
            foreach (ISelectionTool tool in _tools.Values)
                tool.Cancel();
            _labelManager.ClearAll();
            _points = null;
            _sourcePoints = null;
            _viewToSource = null;
            _attributes = null;
            _lasFilePath = "";
            _pendingAction = EditAction.None;
            Mode = InteractionMode.Navigate;
            SetRestingInteractionState();
        }

        public void SetViewConstraint(GripViewConstraint constraint) => ActiveTool.ViewConstraint = constraint;

        /// <summary>
        /// Fit the active tool's primitive tightly to the points currently inside its volume.
        /// Height comes from the fitted OBB by default, or from the average surrounding ground
        /// (class 2) level up to the cluster top when <paramref name="useGround"/> is set.
        /// </summary>
        public string FitActiveToolToSelection(bool useGround)
        {
            if (_points == null || !ActiveTool.IsEditing)
                return "No active selection to fit.";

            var viewIdx = ActiveTool.CreateQuery().Resolve(_points);
            if (viewIdx.Count == 0)
                return "No points inside the selection volume.";

            var pts = new List<Vector3>(viewIdx.Count);
            float clusterMaxZ = float.MinValue;
            foreach (int v in viewIdx)
            {
                var p = new Vector3(_points[v].X, _points[v].Y, _points[v].Z);
                pts.Add(p);
                if (p.Z > clusterMaxZ) clusterMaxZ = p.Z;
            }

            string note;
            switch (ActiveTool)
            {
                case BoxSelectionTool box:
                {
                    ShapeFitter.FitBox(pts, out Vector3 center, out Vector3 half, out Quaternion rot);
                    float footprint = MathF.Max(half.X, half.Y) * 1.1f;
                    (center.Z, half.Z, note) = ResolveHeight(useGround, center.X, center.Y, footprint, center.Z, half.Z, clusterMaxZ);
                    box.Center = center; box.HalfExtents = half; box.Rotation = rot;
                    break;
                }
                case CylinderSelectionTool cyl:
                {
                    ShapeFitter.FitCylinder(pts, out Vector3 center, out float radius, out float halfHeight);
                    float centerZ;
                    (centerZ, halfHeight, note) = ResolveHeight(useGround, center.X, center.Y, radius * 1.1f, center.Z, halfHeight, clusterMaxZ);
                    cyl.Center = new Vector3(center.X, center.Y, centerZ);
                    cyl.Radius = radius; cyl.HalfHeight = halfHeight; cyl.Rotation = Quaternion.Identity;
                    break;
                }
                case SphereSelectionTool sphere:
                {
                    ShapeFitter.FitSphere(pts, out Vector3 center, out float radius);
                    sphere.Center = center; sphere.Radius = radius;
                    note = useGround ? "OBB height (sphere ignores ground)" : "OBB height";
                    break;
                }
                default:
                    return "Active tool does not support fitting.";
            }

            _log($"Fitted {ActiveTool.ToolType} to {pts.Count} points ({note}).");
            return $"Fitted {ActiveTool.ToolType} to {pts.Count} points ({note}).";
        }

        private (float centerZ, float halfZ, string note) ResolveHeight(
            bool useGround, float cx, float cy, float footprintRadius,
            float fittedCenterZ, float fittedHalfZ, float clusterMaxZ)
        {
            if (!useGround)
                return (fittedCenterZ, fittedHalfZ, "OBB height");

            if (GroundAverageZ(cx, cy, footprintRadius) is not float bottom)
                return (fittedCenterZ, fittedHalfZ, "OBB height (no ground points nearby)");

            if (clusterMaxZ <= bottom)
                return (fittedCenterZ, fittedHalfZ, "OBB height (ground above cluster)");

            return ((clusterMaxZ + bottom) * 0.5f, (clusterMaxZ - bottom) * 0.5f, "ground-referenced height");
        }

        // Average Z of ground (class 2) points within a radius of the footprint center.
        private float? GroundAverageZ(float cx, float cy, float radius)
        {
            if (_sourcePoints == null || _attributes == null)
                return null;

            byte[] cls = _attributes.Class;
            int count = System.Math.Min(_sourcePoints.Length, cls.Length);
            float r2 = radius * radius;
            double sum = 0;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (cls[i] != (byte)ClassificationType.Ground)
                    continue;

                float dx = _sourcePoints[i].X - cx;
                float dy = _sourcePoints[i].Y - cy;
                if (dx * dx + dy * dy > r2)
                    continue;

                sum += _sourcePoints[i].Z;
                n++;
            }

            return n > 0 ? (float)(sum / n) : null;
        }

        public void SetMode(InteractionMode mode)
        {
            if (Mode == mode)
                return;

            Mode = mode;
            ActiveTool.Cancel();
            _pendingAction = EditAction.None;
            SetRestingInteractionState();
            _log($"Mode: {Mode}  (tool: {ActiveTool.ToolType}, label: '{_currentLabel}')");
        }

        public void SetTool(SelectionToolType toolType)
        {
            if (ActiveTool.IsEditing || ActiveTool.IsActive)
                ActiveTool.Cancel();

            if (!_tools.ContainsKey(toolType))
                throw new ArgumentOutOfRangeException(nameof(toolType), toolType, "Unknown selection tool.");

            _activeToolType = toolType;

            Mode = InteractionMode.Label;
            _pendingAction = EditAction.None;
            SetRestingInteractionState();
            _log($"Tool: {ActiveTool.ToolType}");
        }

        public void SetLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return;

            _currentLabel = label.Trim();
            _log($"Label: '{_currentLabel}'");
        }

        public bool MouseDownLeft(int x, int y, OrbitCamera camera)
        {
            if (Mode != InteractionMode.Label)
                return false;

            var tool = ActiveTool;
            switch (_interactionState)
            {
                case SelectionInteractionState.ReadyToPlace:
                    tool.OnMouseDown(x, y, camera);
                    SetInteractionState(SelectionInteractionState.Placing);
                    return true;

                case SelectionInteractionState.Placing:
                    return true;

                case SelectionInteractionState.Editing:
                    return BeginEditingGesture(tool, x, y, camera);

                case SelectionInteractionState.DraggingGrip:
                case SelectionInteractionState.KeyboardEdit:
                    return true;

                default:
                    return false;
            }
        }

        public void MouseUpLeft(int x, int y, OrbitCamera camera)
        {
            if (Mode != InteractionMode.Label)
                return;

            var tool = ActiveTool;
            switch (_interactionState)
            {
                case SelectionInteractionState.DraggingGrip:
                    tool.EndHandleDrag();
                    SetInteractionState(SelectionInteractionState.Editing);
                    return;

                case SelectionInteractionState.KeyboardEdit:
                    tool.EndEdit();
                    _pendingAction = EditAction.None;
                    SetInteractionState(SelectionInteractionState.Editing);
                    return;

                case SelectionInteractionState.Placing:
                    tool.OnMouseUp(x, y, camera);
                    if (tool.IsEditing)
                        _log($"{tool.ToolType} placed — use grips or G/S/R, then Enter to label.");
                    SetRestingInteractionState();
                    return;

                default:
                    return;
            }
        }

        public void MouseMove(int x, int y, OrbitCamera camera)
        {
            if (Mode != InteractionMode.Label)
                return;

            var tool = ActiveTool;
            switch (_interactionState)
            {
                case SelectionInteractionState.Placing:
                    tool.OnMouseMove(x, y, camera);
                    break;
                case SelectionInteractionState.DraggingGrip:
                    tool.UpdateHandleDrag(x, y, camera);
                    break;
                case SelectionInteractionState.KeyboardEdit:
                    tool.UpdateEdit(x, y, camera);
                    break;
                case SelectionInteractionState.Editing:
                    tool.HoveredHandle = tool.HitTestHandles(x, y, camera);
                    break;
            }
        }

        public bool MouseWheel(float delta)
        {
            return false;
        }

        public void UpdatePreview(float dt)
        {
            bool active = Mode == InteractionMode.Label && ActiveTool.HasVolume;
            if (active && _points != null)
            {
                _previewWasActive = true;
                _previewTimer += dt;
                if (_previewTimer >= PreviewInterval)
                {
                    _previewTimer = 0f;
                    _previewWorker.Request(ActiveTool.CreateQuery(), _points);
                }
            }
            else if (_previewWasActive)
            {
                _previewWorker.Clear();
                _previewTimer = 999f;
                _previewWasActive = false;
            }
        }

        public bool TryTakePreview(out IReadOnlyList<int>? indices) => _previewWorker.TryTakeLatest(out indices);

        public bool KeyDown(ViewerKey key, bool ctrl)
        {
            if (key == ViewerKey.Escape && Mode == InteractionMode.Label)
            {
                CancelOrExitLabelMode();
                return true;
            }

            if (Mode != InteractionMode.Label)
                return false;

            return true;
        }

        public void Dispose() => _previewWorker.Dispose();

        public void CancelOrExitLabelMode()
        {
            if (_interactionState == SelectionInteractionState.KeyboardEdit)
            {
                ActiveTool.EndEdit();
                _pendingAction = EditAction.None;
                SetInteractionState(SelectionInteractionState.Editing);
            }
            else if (ActiveTool.IsEditing || ActiveTool.IsActive)
            {
                ActiveTool.Cancel();
                _pendingAction = EditAction.None;
                SetRestingInteractionState();
                _log("Selection cancelled");
            }
            else
            {
                Mode = InteractionMode.Navigate;
                SetRestingInteractionState();
                _log("Mode: Navigate");
            }
        }

        public void ConfirmActiveSelection()
        {
            if (!ActiveTool.IsEditing || !ActiveTool.HasVolume)
            {
                _log("No active selection to confirm");
                return;
            }

            if (_points != null)
            {
                var selected = MapToSource(ActiveTool.CreateQuery().Resolve(_points));
                if (selected.Count > 0)
                {
                    _labelManager.ApplyLabel(selected, _currentLabel);
                    _log($"Labeled {selected.Count} points as '{_currentLabel}'  (total: {_labelManager.Count})");
                }
                else
                {
                    _log("No points in selection volume");
                }
            }

            ActiveTool.Confirm();
            _pendingAction = EditAction.None;
            Mode = InteractionMode.Navigate;
            SetRestingInteractionState();
        }

        public bool UndoSelectionCommand()
        {
            if (ActiveTool.IsActive || ActiveTool.IsEditing)
            {
                ActiveTool.Cancel();
                _pendingAction = EditAction.None;
                SetRestingInteractionState();
                _log("Current selection undone");
                return true;
            }

            if (_labelManager.Undo())
            {
                _log($"Undo — labels: {_labelManager.Count}");
                return true;
            }

            _log("Nothing to undo");
            return false;
        }

        // Map visible (ViewPoints) indices returned by a selection query to stable source indices.
        private IReadOnlyCollection<int> MapToSource(IReadOnlyList<int> viewIndices)
        {
            if (_viewToSource == null)
                return viewIndices is IReadOnlyCollection<int> c ? c : viewIndices.ToArray();

            var result = new List<int>(viewIndices.Count);
            foreach (int v in viewIndices)
                if (v >= 0 && v < _viewToSource.Length)
                    result.Add(_viewToSource[v]);
            return result;
        }

        private void SetInteractionState(SelectionInteractionState state)
            => _interactionState = state;

        private void SetRestingInteractionState()
        {
            _interactionState = Mode switch
            {
                InteractionMode.Navigate => SelectionInteractionState.Navigate,
                _ when ActiveTool.IsEditing => SelectionInteractionState.Editing,
                _ when ActiveTool.IsActive  => SelectionInteractionState.Placing,
                _                           => SelectionInteractionState.ReadyToPlace
            };
        }

        private bool BeginEditingGesture(ISelectionTool tool, int x, int y, OrbitCamera camera)
        {
            int handle = tool.HitTestHandles(x, y, camera);
            if (handle >= 0)
            {
                tool.BeginHandleDrag(handle, x, y, camera);
                if (tool.IsHandleDragging)
                    SetInteractionState(SelectionInteractionState.DraggingGrip);
                return true;
            }

            // In a view-constrained (orthographic) viewport, dragging anywhere on the volume
            // body translates it in the view plane — reusing the center grip's drag math.
            if (tool.ViewConstraint.IsConstrained && tool.HitTestBody(x, y, camera))
            {
                tool.BeginHandleDrag(tool.CenterGripIndex, x, y, camera);
                if (tool.IsHandleDragging)
                {
                    SetInteractionState(SelectionInteractionState.DraggingGrip);
                    return true;
                }
            }

            if (_pendingAction == EditAction.None)
                return false;

            switch (_pendingAction)
            {
                case EditAction.Grab:   tool.BeginGrab(x, y, camera);   break;
                case EditAction.Scale:  tool.BeginScale(x, y, camera);  break;
                case EditAction.Rotate: tool.BeginRotate(x, y, camera); break;
            }

            SetInteractionState(SelectionInteractionState.KeyboardEdit);
            return true;
        }

        public bool SaveLabels() => !string.IsNullOrEmpty(_lasFilePath) && SaveLabelsCore();

        public string SaveLabelsToLas()
        {
            if (string.IsNullOrEmpty(_lasFilePath))
                return "No source file is associated with the viewer.";

            var map = new Dictionary<int, byte>();
            foreach (var (idx, name) in _labelManager.AllLabels)
                if (_registry.CodeFor(name) is byte code)
                    map[idx] = code;

            if (map.Count == 0)
                return "No labeled points have a class code. Define labels with LABELDEF or the registry window.";

            string dest = LasClassificationWriter.DefaultDestinationPath(_lasFilePath);
            int written = LasClassificationWriter.Write(_lasFilePath, dest, map);
            return $"Wrote class codes for {written:N0} points to {System.IO.Path.GetFileName(dest)}.";
        }

        public bool LoadLabels() => !string.IsNullOrEmpty(_lasFilePath) && LabelFileIO.Load(_lasFilePath, _labelManager);

        public void ClearLabels() => _labelManager.ClearAll();

        private bool SaveLabelsCore()
        {
            LabelFileIO.Save(_lasFilePath, _labelManager);
            return true;
        }
    }
}
