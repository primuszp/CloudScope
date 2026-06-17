using CloudScope.Labeling;
using CloudScope.Rendering;
using CloudScope.Selection;

namespace CloudScope
{
    public sealed class SelectionController : IDisposable
    {
        private const float PreviewInterval = 0.08f;

        private readonly IReadOnlyDictionary<SelectionToolType, ISelectionTool> _tools;
        private readonly SelectionPreviewWorker _previewWorker = new();
        private readonly LabelManager          _labelManager  = new();
        private readonly Action<string>        _log;

        private SelectionToolType        _activeToolType = SelectionToolType.Box;
        private EditAction               _pendingAction    = EditAction.None;
        private SelectionInteractionState _interactionState = SelectionInteractionState.Navigate;
        private float  _previewTimer    = 999f;
        private bool   _previewWasActive;
        private string _currentLabel   = "Ground";
        private PointData[]? _points;
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
        public PointData[]? Points => _points;
        public bool HasActiveSelection => ActiveTool.IsActive || ActiveTool.IsEditing;

        public void LoadPointCloud(PointData[] points) => _points = points;

        public void SetLasFilePath(string path) => _lasFilePath = path;

        public void Reset()
        {
            foreach (ISelectionTool tool in _tools.Values)
                tool.Cancel();
            _labelManager.ClearAll();
            _points = null;
            _lasFilePath = "";
            _pendingAction = EditAction.None;
            Mode = InteractionMode.Navigate;
            SetRestingInteractionState();
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
                var selected = ActiveTool.CreateQuery().Resolve(_points);
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

        public bool LoadLabels() => !string.IsNullOrEmpty(_lasFilePath) && LabelFileIO.Load(_lasFilePath, _labelManager);

        public void ClearLabels() => _labelManager.ClearAll();

        private bool SaveLabelsCore()
        {
            LabelFileIO.Save(_lasFilePath, _labelManager);
            return true;
        }
    }
}
