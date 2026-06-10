using System.Numerics;
using CloudScope.Commands;
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
        }

        private void RenderMainMenu()
        {
            if (!ImGui.BeginMainMenuBar())
                return;

            if (ImGui.BeginMenu("File"))
            {
                ImGui.MenuItem("Open LAS", null, false, false);
                if (ImGui.MenuItem("Save Labels")) Stage("SAVELABELS");
                if (ImGui.MenuItem("Load Labels")) Stage("LOADLABELS");
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Label"))
            {
                if (ImGui.MenuItem("Box"))      Stage("SELECT B");
                if (ImGui.MenuItem("Sphere"))   Stage("SELECT S");
                if (ImGui.MenuItem("Cylinder")) Stage("SELECT C");
                ImGui.Separator();
                if (ImGui.MenuItem("Confirm")) Stage("CONFIRM");
                if (ImGui.MenuItem("Undo"))    Stage("UNDO");
                if (ImGui.MenuItem("Cancel"))  Stage("CANCEL");
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Mode"))
            {
                if (ImGui.MenuItem("Navigate", null, _viewer.Mode == InteractionMode.Navigate)) Stage("NAVIGATE");
                if (ImGui.MenuItem("Label",    null, _viewer.Mode == InteractionMode.Label))    Stage("LABELMODE");
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

        private void Execute(string command) => _session.Submit(command);

        private void Stage(string command)
        {
            _session.Stage(command);
            _commandText = command;
            _focusCommandLine = true;
        }
    }
}
