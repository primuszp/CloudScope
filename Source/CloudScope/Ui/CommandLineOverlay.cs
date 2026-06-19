using System;
using System.Numerics;
using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Platform.Windows;
using CloudScope.Selection;
using ImGuiNET;

namespace CloudScope.Ui
{
    public sealed class CommandLineOverlay
    {
        private readonly ViewerController _viewer;
        private readonly ViewerCommandDispatcher _dispatcher;
        private readonly CommandLineSession _session;
        private string _commandText = "";
        private bool _focusCommandLine;
        private bool _submitRequested;
        private string _labelNameInput = "";
        private int _labelCodeInput = 6;

        public CommandLineOverlay(ViewerController viewer, ViewerCommandDispatcher dispatcher)
        {
            _viewer = viewer;
            _dispatcher = dispatcher;
            _session = dispatcher.Session;
        }

        public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;
        public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

        public void FocusCommandLine() => _focusCommandLine = true;

        public void Submit() => _submitRequested = true;

        public void Cancel()
        {
            Execute("CANCEL");
            _commandText = "";
            _focusCommandLine = true;
        }

        public void Render(int width, int height)
        {
            RenderMainMenu();
            RenderCommandLine(width, height);
            RenderLabelWindow();
        }

        private void RenderMainMenu()
        {
            if (!ImGui.BeginMainMenuBar())
                return;

            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Open LAS"))    OpenLas();
                if (ImGui.MenuItem("Save Labels (JSON)")) Run("SAVELABELS");
                if (ImGui.MenuItem("Save Labels to LAS")) Run("SAVELABELS LAS");
                if (ImGui.MenuItem("Load Labels")) Run("LOADLABELS");
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Label"))
            {
                if (ImGui.MenuItem("Box"))      Run("SELECT B");
                if (ImGui.MenuItem("Sphere"))   Run("SELECT S");
                if (ImGui.MenuItem("Cylinder")) Run("SELECT C");
                ImGui.Separator();
                if (ImGui.MenuItem("Fit to points")) Run("FIT");
                if (ImGui.MenuItem("Fit to ground")) Run("FITGROUND");
                if (ImGui.MenuItem("Confirm")) Run("CONFIRM");
                if (ImGui.MenuItem("Undo"))    Run("UNDO");
                if (ImGui.MenuItem("Cancel"))  Run("CANCEL");
                ImGui.Separator();
                if (ImGui.MenuItem("Label registry...", null, _viewer.LabelWindowVisible)) _viewer.ToggleLabelWindow();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Mode"))
            {
                if (ImGui.MenuItem("Navigate", null, _viewer.Mode == InteractionMode.Navigate)) Run("NAVIGATE");
                if (ImGui.MenuItem("Label",    null, _viewer.Mode == InteractionMode.Label))    Run("LABELMODE");
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Perspective", null, _viewer.IsPerspective))  Run("PROJECTION PERSPECTIVE");
                if (ImGui.MenuItem("Parallel",    null, !_viewer.IsPerspective)) Run("PROJECTION PARALLEL");
                ImGui.EndMenu();
            }

            ImGui.TextDisabled($"Mode: {_viewer.Mode}  Tool: {_viewer.ActiveToolType}");
            ImGui.EndMainMenuBar();
        }

        private void RenderCommandLine(int width, int height)
        {
            const float commandHeight = 86f;
            ImGui.SetNextWindowPos(new Vector2(0f, height - commandHeight), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(width, commandHeight), ImGuiCond.Always);

            ImGuiWindowFlags flags =
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoCollapse;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.04f, 0.045f, 0.055f, 0.92f));
            ImGui.Begin("CloudScopeCommandLine", flags);

            int start = Math.Max(0, _session.History.Count - 2);
            for (int i = start; i < _session.History.Count; i++)
                ImGui.TextUnformatted(_session.History[i]);

            ImGui.Separator();
            ImGui.TextUnformatted(_dispatcher.CurrentPrompt);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1f);

            if (_focusCommandLine)
            {
                ImGui.SetKeyboardFocusHere();
                _focusCommandLine = false;
            }

            bool upPressed   = ImGui.IsKeyPressed(ImGuiKey.UpArrow,   false);
            bool downPressed = ImGui.IsKeyPressed(ImGuiKey.DownArrow, false);

            bool submitted = ImGui.InputText(
                "##CommandInput",
                ref _commandText,
                256,
                ImGuiInputTextFlags.EnterReturnsTrue);

            if (ImGui.IsItemActive())
            {
                if (upPressed)   _commandText = _session.Recall(-1);
                if (downPressed) _commandText = _session.Recall(1);
            }

            if (submitted || _submitRequested)
            {
                _submitRequested = false;
                Execute(_commandText);
                _commandText = "";
                _focusCommandLine = true;
            }

            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }

        private void RenderLabelWindow()
        {
            if (!_viewer.LabelWindowVisible)
                return;

            bool open = true;
            ImGui.SetNextWindowSize(new Vector2(360f, 420f), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Label registry", ref open))
            {
                ImGui.TextDisabled("Map label names to LAS classification codes.");
                ImGui.Separator();

                foreach (LabelDefinition def in _viewer.LabelRegistry.Definitions)
                {
                    ImGui.PushID(def.Name);
                    ImGui.ColorButton("##swatch", new Vector4(def.Color.X, def.Color.Y, def.Color.Z, 1f),
                        ImGuiColorEditFlags.NoTooltip, new Vector2(16f, 16f));
                    ImGui.SameLine();
                    bool active = string.Equals(def.Name, _viewer.CurrentLabel, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable($"{def.Name}  (class {def.Code}){(active ? "  *" : "")}", active))
                        _viewer.SetActiveLabel(def.Name);
                    ImGui.PopID();
                }

                ImGui.Separator();
                ImGui.TextUnformatted("Add / update");
                ImGui.SetNextItemWidth(180f);
                ImGui.InputText("Name", ref _labelNameInput, 64);
                ImGui.SetNextItemWidth(120f);
                ImGui.InputInt("Class code", ref _labelCodeInput);
                _labelCodeInput = Math.Clamp(_labelCodeInput, 0, 255);
                if (ImGui.Button("Define") && _labelNameInput.Trim().Length > 0)
                {
                    string name = _labelNameInput.Trim();
                    _viewer.DefineLabel(name, (byte)_labelCodeInput);
                    _viewer.SetActiveLabel(name);
                }
            }

            ImGui.End();
            _viewer.LabelWindowVisible = open;
        }

        private void Execute(string command) => _session.Submit(command);

        private void OpenLas()
        {
            if (!WindowsOpenFileDialog.IsAvailable)
            {
                Run("OPEN");
                return;
            }

            Stage("OPEN");
            Execute("OPEN");

            string? path = WindowsOpenFileDialog.PickLasFile();
            Execute(path != null ? $"\"{path}\"" : "CANCEL");
            _commandText = "";
            _focusCommandLine = true;
        }

        private void Run(string command)
        {
            Stage(command);
            Execute(command);
            _commandText = "";
            _focusCommandLine = true;
        }

        private void Stage(string command)
        {
            _session.Stage(command);
            _commandText = command;
            _focusCommandLine = true;
        }
    }
}
