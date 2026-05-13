using System.Numerics;
using CloudScope.Selection;
using ImGuiNET;

namespace CloudScope.Ui
{
    public sealed class CommandLineOverlay
    {
        private readonly ViewerController _viewer;
        private readonly ViewerCommandDispatcher _dispatcher;
        private readonly List<string> _history = new();
        private string _commandText = "";
        private bool _focusCommandLine;

        public CommandLineOverlay(ViewerController viewer)
        {
            _viewer = viewer;
            _dispatcher = new ViewerCommandDispatcher(viewer);
            _history.Add("Command line ready. Type HELP for commands.");
        }

        public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;
        public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

        public void FocusCommandLine() => _focusCommandLine = true;

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
                ImGui.MenuItem("Save Labels", "Ctrl+S");
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Label"))
            {
                if (ImGui.MenuItem("Box", "B")) Execute("SELECT B");
                if (ImGui.MenuItem("Sphere")) Execute("SELECT S");
                if (ImGui.MenuItem("Cylinder")) Execute("SELECT C");
                ImGui.Separator();
                if (ImGui.MenuItem("Confirm", "Enter")) Execute("CONFIRM");
                if (ImGui.MenuItem("Cancel", "Esc")) Execute("CANCEL");
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Mode"))
            {
                if (ImGui.MenuItem("Navigate", null, _viewer.Mode == InteractionMode.Navigate)) Execute("NAVIGATE");
                if (ImGui.MenuItem("Label", null, _viewer.Mode == InteractionMode.Label)) Execute("LABELMODE");
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

            int start = Math.Max(0, _history.Count - 2);
            for (int i = start; i < _history.Count; i++)
                ImGui.TextUnformatted(_history[i]);

            ImGui.Separator();
            ImGui.TextUnformatted("Command:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1f);

            if (_focusCommandLine)
            {
                ImGui.SetKeyboardFocusHere();
                _focusCommandLine = false;
            }

            bool submitted = ImGui.InputText(
                "##CommandInput",
                ref _commandText,
                256,
                ImGuiInputTextFlags.EnterReturnsTrue);

            if (submitted)
            {
                Execute(_commandText);
                _commandText = "";
                _focusCommandLine = true;
            }

            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }

        private void Execute(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            _history.Add($"> {command.Trim()}");
            string result = _dispatcher.Execute(command);
            if (!string.IsNullOrWhiteSpace(result))
                _history.Add(result);

            while (_history.Count > 32)
                _history.RemoveAt(0);
        }
    }
}
