using CloudScope.Labeling;
using CloudScope.Rendering;
using CloudScope.Selection;

namespace CloudScope
{
    public sealed class SelectionController : IDisposable
    {
        private const float PreviewInterval = 0.08f;

        private readonly BoxSelectionTool _boxTool = new();
        private readonly SphereSelectionTool _sphereTool = new();
        private readonly CylinderSelectionTool _cylinderTool = new();
        private readonly ISelectionTool[] _tools;
        private readonly SelectionPreviewWorker _previewWorker = new();
        private readonly LabelManager _labelManager = new();

        private int _activeToolIndex;
        private EditAction _pendingAction = EditAction.None;
        private bool _editDragging;
        private float _previewTimer = 999f;
        private bool _previewWasActive;
        private string _currentLabel = "Ground";
        private PointData[]? _points;
        private string _lasFilePath = "";

        private static readonly string[] LabelPresets =
            { "Ground", "Building", "Vegetation", "Vehicle", "Road", "Water", "Wire" };

        public SelectionController(Action labelsChanged)
        {
            _tools = new ISelectionTool[] { _boxTool, _sphereTool, _cylinderTool };
            _labelManager.LabelsChanged += labelsChanged;
        }

        public InteractionMode Mode { get; private set; } = InteractionMode.Navigate;
        public ISelectionTool ActiveTool => _tools[_activeToolIndex];
        public LabelManager Labels => _labelManager;
        public PointData[]? Points => _points;

        public void LoadPointCloud(PointData[] points) => _points = points;

        public void SetLasFilePath(string path) => _lasFilePath = path;

        public void SetMode(InteractionMode mode)
        {
            if (Mode == mode)
                return;

            Mode = mode;
            ActiveTool.Cancel();
            _pendingAction = EditAction.None;
            _editDragging = false;
            Console.WriteLine($"Mode: {Mode}  (tool: {ActiveTool.ToolType}, label: '{_currentLabel}')");
        }

        public void SetTool(SelectionToolType toolType)
        {
            if (ActiveTool.IsEditing || ActiveTool.IsActive)
                ActiveTool.Cancel();

            _activeToolIndex = toolType switch
            {
                SelectionToolType.Box => 0,
                SelectionToolType.Sphere => 1,
                SelectionToolType.Cylinder => 2,
                _ => _activeToolIndex
            };

            Mode = InteractionMode.Label;
            Console.WriteLine($"Tool: {ActiveTool.ToolType}");
        }

        public void SetLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return;

            _currentLabel = label.Trim();
            Console.WriteLine($"Label: '{_currentLabel}'");
        }

        public bool MouseDownLeft(int x, int y, OrbitCamera camera)
        {
            if (Mode != InteractionMode.Label)
                return false;

            var tool = ActiveTool;
            if (tool.Phase == ToolPhase.Idle)
            {
                tool.OnMouseDown(x, y, camera);
                return true;
            }

            if (tool.Phase == ToolPhase.Drawing)
                return true;

            if (tool.Phase != ToolPhase.Editing)
                return false;

            int handle = tool.HitTestHandles(x, y, camera);
            if (handle >= 0)
            {
                tool.BeginHandleDrag(handle, x, y, camera);
                return true;
            }

            if (_pendingAction == EditAction.None)
                return false;

            switch (_pendingAction)
            {
                case EditAction.Grab: tool.BeginGrab(x, y, camera); break;
                case EditAction.Scale: tool.BeginScale(x, y, camera); break;
                case EditAction.Rotate: tool.BeginRotate(x, y, camera); break;
            }

            _editDragging = true;
            return true;
        }

        public void MouseUpLeft(int x, int y, OrbitCamera camera)
        {
            if (Mode != InteractionMode.Label)
                return;

            var tool = ActiveTool;
            if (tool.IsHandleDragging)
            {
                tool.EndHandleDrag();
                return;
            }

            if (_editDragging)
            {
                tool.EndEdit();
                _editDragging = false;
                _pendingAction = EditAction.None;
                return;
            }

            if (tool.Phase != ToolPhase.Drawing)
                return;

            tool.OnMouseUp(x, y, camera);
            if (tool.IsEditing)
                Console.WriteLine($"{tool.ToolType} selection placed - use grips or G/S/R, then Enter to label.");
        }

        public void MouseMove(int x, int y, OrbitCamera camera)
        {
            if (Mode != InteractionMode.Label)
                return;

            var tool = ActiveTool;
            if (tool.Phase == ToolPhase.Drawing)
                tool.OnMouseMove(x, y, camera);
            else if (tool.IsHandleDragging)
                tool.UpdateHandleDrag(x, y, camera);
            else if (_editDragging)
                tool.UpdateEdit(x, y, camera);
            else if (tool.Phase == ToolPhase.Editing)
                tool.HoveredHandle = tool.HitTestHandles(x, y, camera);
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

        public void KeyDown(ViewerKey key, bool ctrl)
        {
            if (key == ViewerKey.L)
            {
                SetMode(Mode == InteractionMode.Navigate ? InteractionMode.Label : InteractionMode.Navigate);
            }

            if (key == ViewerKey.Escape && Mode == InteractionMode.Label)
            {
                CancelOrExitLabelMode();
                return;
            }

            if (Mode != InteractionMode.Label)
                return;

            if (key == ViewerKey.Enter && ActiveTool.IsEditing)
                ConfirmActiveSelection();

            if (ActiveTool.IsEditing)
                HandleEditShortcut(key);

            if (!ActiveTool.IsEditing && !ActiveTool.IsActive)
                HandleToolSwitch(key);

            HandleLabelShortcut(key);

            if (ctrl && key == ViewerKey.Z && _labelManager.Undo())
                Console.WriteLine($"Undo — labels: {_labelManager.Count}");

            if (ctrl && key == ViewerKey.S && !string.IsNullOrEmpty(_lasFilePath))
                LabelFileIO.Save(_lasFilePath, _labelManager);
            if (ctrl && key == ViewerKey.O && !string.IsNullOrEmpty(_lasFilePath))
                LabelFileIO.Load(_lasFilePath, _labelManager);
        }

        public void Dispose() => _previewWorker.Dispose();

        public void CancelOrExitLabelMode()
        {
            if (_editDragging)
            {
                ActiveTool.EndEdit();
                _editDragging = false;
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
                Mode = InteractionMode.Navigate;
                Console.WriteLine("Mode: Navigate");
            }
        }

        public void ConfirmActiveSelection()
        {
            if (_points != null)
            {
                var selected = ActiveTool.CreateQuery().Resolve(_points);
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

        private void HandleEditShortcut(ViewerKey key)
        {
            if (key == ViewerKey.G) { _pendingAction = EditAction.Grab; Console.WriteLine("Grab — left-click + drag to move"); }
            if (key == ViewerKey.S) { _pendingAction = EditAction.Scale; Console.WriteLine("Scale — left-click + drag to resize"); }
            if (key == ViewerKey.R) { _pendingAction = EditAction.Rotate; Console.WriteLine("Rotate — left-click + drag to rotate"); }
            if (key == ViewerKey.X) { ActiveTool.SetAxisConstraint(0); Console.WriteLine("Constraint: X axis"); }
            if (key == ViewerKey.Y) { ActiveTool.SetAxisConstraint(1); Console.WriteLine("Constraint: Y axis"); }
            if (key == ViewerKey.Z) { ActiveTool.SetAxisConstraint(2); Console.WriteLine("Constraint: Z axis"); }
        }

        private void HandleToolSwitch(ViewerKey key)
        {
            if (key == ViewerKey.D1) SetTool(SelectionToolType.Box);
            if (key == ViewerKey.D2) SetTool(SelectionToolType.Sphere);
            if (key == ViewerKey.D3) SetTool(SelectionToolType.Cylinder);
        }

        private void HandleLabelShortcut(ViewerKey key)
        {
            ViewerKey[] presetKeys = { ViewerKey.D4, ViewerKey.D5, ViewerKey.D6, ViewerKey.D7, ViewerKey.D8, ViewerKey.D9 };
            for (int i = 0; i < presetKeys.Length && i < LabelPresets.Length; i++)
            {
                if (key == presetKeys[i])
                {
                    SetLabel(LabelPresets[i]);
                }
            }
        }
    }
}
