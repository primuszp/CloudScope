using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Loading;
using CloudScope.Platform.Windows;
using CloudScope.Selection;
using ImGuiNET;

namespace CloudScope.Ui
{
    /// <summary>
    /// The GameWindow shell's entire user interface, rendered as ImGui panels over the
    /// viewport: menu bar, tool strip, properties panel, label registry, status bar, and the
    /// AutoCAD-style command window that is the centre of the application.
    /// </summary>
    public sealed class CommandLineOverlay
    {
        private const int CommandInputCapacity = 512;
        private const float MinCommandLines = 1f;
        private const float MaxCommandLines = 12f;

        private readonly ViewerController _viewer;
        private readonly ViewerCommandDispatcher _dispatcher;
        private readonly CommandLineSession _session;
        private readonly IReadOnlyList<CommandMenuEntry> _menu = CommandMenu.Build();
        private readonly float _scale;
        private readonly ShellSettings _settings = ShellSettings.Load();

        private string _commandText = "";
        private bool _focusCommandLine = true;
        private bool _submitRequested;
        private float _commandLines = 5f;
        private bool _autoScrollHistory = true;

        // Completion state
        private IReadOnlyList<CommandCompletion> _completions = [];
        private int _completionIndex;
        private string _completionPrefix = "";

        private string _labelNameInput = "";
        private int _labelCodeInput = 6;
        private int _instanceIdInput = 1;

        public CommandLineOverlay(ViewerController viewer, ViewerCommandDispatcher dispatcher, float uiScale = 1f)
        {
            _viewer = viewer;
            _dispatcher = dispatcher;
            _session = dispatcher.Session;
            _scale = uiScale > 0f ? uiScale : 1f;
            _commandLines = (float)Math.Clamp(_settings.HeightInLines, MinCommandLines, MaxCommandLines);
            _session.SeedInputHistory(_settings.RecentInput);
        }

        public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;
        public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

        public void FocusCommandLine() => _focusCommandLine = true;

        public void Submit() => _submitRequested = true;

        public void Cancel()
        {
            if (_completions.Count > 0)
            {
                DismissCompletions();
                return;
            }

            Execute("CANCEL");
            _commandText = "";
            _focusCommandLine = true;
        }

        /// <summary>Persists the command window height and input history on shutdown.</summary>
        public void SaveSettings()
        {
            _settings.HeightInLines = _commandLines;
            _settings.RecentInput = _session.InputHistory.ToList();
            _settings.Save();
        }

        public void ToggleHistoryWindow() => _viewer.CommandHistoryVisible = !_viewer.CommandHistoryVisible;

        /// <param name="width">Framebuffer width in physical pixels.</param>
        /// <param name="height">Framebuffer height in physical pixels.</param>
        public void Render(int width, int height)
        {
            ViewerStatusSnapshot status = _viewer.Status;
            HandleCommandWindowToggle();

            float menuHeight = RenderMainMenu(status);
            float toolStripHeight = RenderToolStrip(menuHeight, width, status);
            float statusHeight = RenderStatusBar(width, height, status);
            float commandHeight = status.CommandLineVisible
                ? RenderCommandWindow(width, height - statusHeight, status.CommandLineFloating)
                : 0f;

            float contentTop = menuHeight + toolStripHeight;
            float contentBottom = height - statusHeight - commandHeight;
            float propertiesWidth = RenderPropertiesPanel(width, contentTop, contentBottom - contentTop, status);
            RenderLabelWindow();
            RenderHistoryWindow(width, height);

            // Hand the viewer the rectangle the panels leave free, so the crosshair, the
            // projection aspect and picking all refer to the view the user actually sees.
            _viewer.SetViewportInset(
                left: 0,
                top: (int)MathF.Ceiling(contentTop),
                right: (int)MathF.Ceiling(propertiesWidth),
                bottom: (int)MathF.Ceiling(statusHeight + commandHeight));
        }

        // ── Menu bar ────────────────────────────────────────────────────────────

        private float RenderMainMenu(ViewerStatusSnapshot status)
        {
            if (!ImGui.BeginMainMenuBar())
                return 0f;

            float height = ImGui.GetWindowSize().Y;

            foreach (CommandMenuEntry group in _menu)
            {
                if (!ImGui.BeginMenu(group.Header))
                    continue;

                RenderMenuItems(group.Items, status);
                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
            return height;
        }

        private void RenderMenuItems(IReadOnlyList<CommandMenuEntry> items, ViewerStatusSnapshot status)
        {
            foreach (CommandMenuEntry item in items)
            {
                if (item.IsSeparator)
                {
                    ImGui.Separator();
                    continue;
                }

                if (item.IsSubmenu)
                {
                    if (ImGui.BeginMenu(item.Header))
                    {
                        RenderMenuItems(item.Items, status);
                        ImGui.EndMenu();
                    }

                    continue;
                }

                bool selected = item.CheckState.Length > 0 && CommandMenu.IsChecked(item.CheckState, status);
                bool enabled = !item.RequiresCloud || status.HasCloud;
                if (ImGui.MenuItem(item.Header, CommandMenu.DisplayShortcut(item.Shortcut, OperatingSystem.IsMacOS()), selected, enabled))
                    Activate(item.Command);
            }
        }

        // The OPEN command needs a file dialog on platforms that have one; everything else is
        // just command text, so menus, the tool strip and typing stay the same code path.
        private void Activate(string command)
        {
            if (command.Equals("OPEN", StringComparison.OrdinalIgnoreCase))
            {
                OpenLas();
                return;
            }

            if (command.Equals("HISTORY", StringComparison.OrdinalIgnoreCase))
            {
                ToggleHistoryWindow();
                return;
            }

            Run(command);
        }

        // ── Tool strip ──────────────────────────────────────────────────────────

        private float RenderToolStrip(float top, int width, ViewerStatusSnapshot status)
        {
            float height = MathF.Round(40f * _scale);
            ImGui.SetNextWindowPos(new Vector2(0f, top), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f * _scale, 5f * _scale));

            if (ImGui.Begin("##CloudScopeToolStrip", FixedPanelFlags))
            {
                ToolButton("Open", "OPEN", false, "Open a LAS/LAZ point cloud");
                ToolSeparator();
                ToolButton("Navigate", "NAVIGATE", status.Mode == InteractionMode.Navigate, "Orbit / pan / zoom");
                ToolButton("Label", "LABELMODE", status.Mode == InteractionMode.Label, "Place and edit selections");
                ToolSeparator();
                ToolButton("Box", "SELECT Box", status.ActiveTool == SelectionToolType.Box, "Box selection");
                ToolButton("Sphere", "SELECT Sphere", status.ActiveTool == SelectionToolType.Sphere, "Sphere selection");
                ToolButton("Cylinder", "SELECT Cylinder", status.ActiveTool == SelectionToolType.Cylinder, "Cylinder selection");
                ToolSeparator();
                ToolButton("Fit", "FIT Points", false, "Fit the selection to the points inside it");
                ToolButton("Confirm", "CONFIRM", false, "Apply the active selection");
                ToolButton("Cancel", "CANCEL", false, "Cancel the active command");
                ToolSeparator();
                ToolButton("Zoom extents", "ZOOM Extents", false, "Fit the whole cloud in the view");
            }

            ImGui.End();
            ImGui.PopStyleVar();
            return height;
        }

        private void ToolButton(string label, string command, bool active, string tooltip)
        {
            if (active)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Accent with { W = 0.55f });

            if (ImGui.Button(label))
                Activate(command);

            if (active)
                ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{tooltip}\n{command}");

            ImGui.SameLine();
        }

        private void ToolSeparator()
        {
            ImGui.TextDisabled("|");
            ImGui.SameLine();
        }

        // ── Properties panel ────────────────────────────────────────────────────

        /// <returns>Width of the panel, so the caller can keep the viewport clear of it.</returns>
        private float RenderPropertiesPanel(int width, float top, float height, ViewerStatusSnapshot status)
        {
            float panelWidth = MathF.Round(260f * _scale);
            ImGui.SetNextWindowPos(new Vector2(width - panelWidth, top), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(panelWidth, height), ImGuiCond.Always);

            if (!ImGui.Begin("Properties", FixedPanelFlags | ImGuiWindowFlags.NoTitleBar))
            {
                ImGui.End();
                return panelWidth;
            }

            ImGui.TextDisabled("PROPERTIES");
            ImGui.Separator();

            if (ImGui.CollapsingHeader("General", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Property("File", status.SourceName.Length > 0 ? status.SourceName : "—");
                Property("Points", status.PointCountText);
                Property("Filter", status.Filter.Length > 0 ? status.Filter : "None");
            }

            if (ImGui.CollapsingHeader("View", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Property("Projection", status.ProjectionText);
                Property("View", status.ViewName);
                Property("Layout", status.ViewportLayout);
                Property("FPS", status.Fps.ToString("0"));
            }

            if (ImGui.CollapsingHeader("Display", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Property("Color by", status.ColorSource.ToDisplayName());

                // Editing round-trips through the command line so the history stays a complete
                // record of what changed the document, exactly as typing POINTSIZE would.
                float pointSize = status.PointSize;
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.SliderFloat("##pointsize", ref pointSize, 1f, 10f, "Point size %.0f px") &&
                    MathF.Abs(pointSize - status.PointSize) >= 0.5f)
                {
                    Execute($"POINTSIZE {MathF.Round(pointSize)}");
                }
            }

            if (ImGui.CollapsingHeader("Labeling", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Property("Mode", status.Mode.ToString());
                Property("Tool", status.ActiveTool.ToString());
                Property("State", status.InteractionState.ToString());
                Property("Label", status.CurrentLabel.Length > 0 ? status.CurrentLabel : "—");
                Property("Instance", status.InstanceText);
                if (ImGui.Button("Label registry"))
                    Run("LABELS");
            }

            ImGui.End();
            return panelWidth;
        }

        private void Property(string name, string value)
        {
            ImGui.TextDisabled(name);
            ImGui.SameLine(MathF.Round(96f * _scale));
            ImGui.TextUnformatted(value);
        }

        // ── Command window ──────────────────────────────────────────────────────

        // Ctrl+9 is AutoCAD's command-window toggle, and it has to keep working while the
        // window is gone — it is the only way back to it in this shell.
        private void HandleCommandWindowToggle()
        {
            if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey._9, repeat: false))
                Run("COMMANDLINE Toggle");
        }

        /// <param name="floating">
        /// True when the command window floats over the drawing: it is then moved and sized by
        /// the user, and reserves no room, so the viewport keeps the space underneath it.
        /// </param>
        /// <returns>Height to keep the viewport clear of, which is none while floating.</returns>
        private float RenderCommandWindow(int width, float bottom, bool floating)
        {
            float lineHeight = ImGui.GetTextLineHeightWithSpacing();
            float chrome = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y * 2f;
            float height = MathF.Round(_commandLines * lineHeight + chrome);
            float top = bottom - height;

            ImGui.SetNextWindowPos(new Vector2(0f, top), floating ? ImGuiCond.FirstUseEver : ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(width, height), floating ? ImGuiCond.FirstUseEver : ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * _scale, 6f * _scale));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, ImGuiTheme.SurfaceDeep);
            ImGui.Begin(floating ? "Command" : "##CloudScopeCommandLine",
                floating ? ImGuiWindowFlags.NoSavedSettings : FixedPanelFlags);

            if (floating)
            {
                height = ImGui.GetWindowSize().Y;
                top = ImGui.GetWindowPos().Y;
            }

            if (!floating)
                RenderResizeGrip(width, top, lineHeight);

            RenderContextMenu();
            RenderHistoryLines(height - chrome);
            RenderPromptLine();

            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();

            RenderCompletionPopup(top);
            return floating ? 0f : height;
        }

        // A thin drag strip along the top edge resizes the command window, the way the
        // AutoCAD command line is resized by dragging its border.
        private void RenderResizeGrip(int width, float top, float lineHeight)
        {
            Vector2 cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(0f, top));
            ImGui.InvisibleButton("##CommandResize", new Vector2(width, MathF.Max(4f, 4f * _scale)));

            if (ImGui.IsItemHovered() || ImGui.IsItemActive())
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);

            if (ImGui.IsItemActive())
            {
                float delta = ImGui.GetIO().MouseDelta.Y;
                _commandLines = Math.Clamp(_commandLines - delta / lineHeight, MinCommandLines, MaxCommandLines);
            }

            ImGui.SetCursorScreenPos(cursor);
        }

        private void RenderHistoryLines(float height)
        {
            if (ImGui.BeginChild("##CommandHistory", new Vector2(0f, height), ImGuiChildFlags.None,
                    ImGuiWindowFlags.NoBackground))
            {
                PushMono();
                RenderEntries(_session.History);
                PopMono();

                if (_autoScrollHistory)
                {
                    ImGui.SetScrollHereY(1f);
                    _autoScrollHistory = false;
                }
            }

            ImGui.EndChild();
        }

        // Only the handful of lines actually on screen are submitted; the transcript holds
        // hundreds and this runs every frame.
        private static unsafe void RenderEntries(IReadOnlyList<CommandLineEntry> entries)
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(entries.Count);

            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    CommandLineEntry entry = entries[i];
                    ImGui.TextColored(ImGuiTheme.EntryColor(entry.Kind), entry.Text);
                }
            }

            clipper.End();
            clipper.Destroy();
        }

        private void RenderPromptLine()
        {
            PushMono();

            // Keep the prompt and the editable suffix visually continuous.  The input frame
            // is intentionally transparent, so this is one console line rather than a label
            // followed by a separate widget.
            ImGui.TextColored(ImGuiTheme.Accent, _dispatcher.CurrentPrompt.TrimEnd() + " ");
            ImGui.SameLine(0f, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);

            ImGui.SetNextItemWidth(-1f);

            if (_focusCommandLine)
            {
                ImGui.SetKeyboardFocusHere();
                _focusCommandLine = false;
            }

            bool submitted = ImGui.InputText("##CommandInput", ref _commandText, CommandInputCapacity,
                ImGuiInputTextFlags.EnterReturnsTrue);

            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar();

            if (ImGui.IsItemActive() || ImGui.IsItemFocused())
                HandleCommandKeys();

            PopMono();

            if (submitted || _submitRequested)
            {
                _submitRequested = false;
                Execute(_commandText);
                _commandText = "";
                _focusCommandLine = true;
            }
        }

        private void HandleCommandKeys()
        {
            bool listOpen = _completions.Count > 0;

            if (ImGui.IsKeyPressed(ImGuiKey.Tab, false))
            {
                CycleCompletion(1);
                _focusCommandLine = true;
                return;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, false))
            {
                if (listOpen) CycleCompletion(-1);
                else _commandText = _session.Recall(-1);
                return;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, false))
            {
                if (listOpen) CycleCompletion(1);
                else _commandText = _session.Recall(1);
                return;
            }

            // Space submits like Enter, except where the active prompt takes free-form text
            // (a path, a value list) — there a space is just a space.
            if (_session.SpaceSubmits && ImGui.IsKeyPressed(ImGuiKey.Space, false) && _commandText.Trim().Length > 0)
            {
                _commandText = _commandText.TrimEnd();
                _submitRequested = true;
                return;
            }

            RefreshCompletions();
        }

        private void RefreshCompletions()
        {
            string typed = _commandText.Trim();
            if (typed == _completionPrefix)
                return;

            _completionPrefix = typed;
            _completions = _session.Complete(typed);
            _completionIndex = 0;
        }

        private void CycleCompletion(int direction)
        {
            if (_completions.Count == 0)
            {
                RefreshCompletions();
                if (_completions.Count == 0)
                    return;
            }

            _completionIndex = (_completionIndex + direction + _completions.Count) % _completions.Count;
            _commandText = _completions[_completionIndex].Insert;
            _completionPrefix = _commandText;
        }

        private void DismissCompletions()
        {
            _completions = [];
            _completionIndex = 0;
            _completionPrefix = _commandText.Trim();
        }

        // The list is drawn above the input: the command window is already at the bottom edge.
        private void RenderCompletionPopup(float commandWindowTop)
        {
            if (_completions.Count == 0)
                return;

            float rowHeight = ImGui.GetTextLineHeightWithSpacing();
            float popupHeight = _completions.Count * rowHeight + ImGui.GetStyle().WindowPadding.Y * 2f;
            float popupWidth = MathF.Round(320f * _scale);

            ImGui.SetNextWindowPos(new Vector2(MathF.Round(12f * _scale), commandWindowTop - popupHeight), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(popupWidth, popupHeight), ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, ImGuiTheme.SurfaceDeep with { W = 0.97f });

            if (ImGui.Begin("##CommandCompletions", FixedPanelFlags | ImGuiWindowFlags.NoFocusOnAppearing))
            {
                for (int i = 0; i < _completions.Count; i++)
                {
                    CommandCompletion completion = _completions[i];
                    if (ImGui.Selectable($"{completion.Display}##completion{i}", i == _completionIndex))
                    {
                        _commandText = completion.Insert;
                        DismissCompletions();
                        _submitRequested = true;
                    }

                    ImGui.SameLine(popupWidth - MathF.Round(110f * _scale));
                    ImGui.TextDisabled(completion.Detail);
                }
            }

            ImGui.End();
            ImGui.PopStyleColor();
        }

        private void RenderContextMenu()
        {
            if (!ImGui.BeginPopupContextWindow("##CommandContext"))
                return;

            if (ImGui.BeginMenu("Recent commands"))
            {
                RenderRecentItems(_session.RecentCommands);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Recent input"))
            {
                RenderRecentItems(_session.RecentInput);
                ImGui.EndMenu();
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Copy history"))
                ImGui.SetClipboardText(_session.HistoryText);

            if (ImGui.MenuItem("Clear history"))
                _session.ClearHistory();

            ImGui.Separator();
            if (ImGui.MenuItem("Expanded history", "F2"))
                ToggleHistoryWindow();

            ImGui.EndPopup();
        }

        private void RenderRecentItems(IEnumerable<string> commands)
        {
            bool any = false;
            foreach (string command in commands.Take(12))
            {
                any = true;
                if (ImGui.MenuItem(command))
                    Run(command);
            }

            if (!any)
                ImGui.TextDisabled("Nothing yet");
        }

        // ── Status bar ──────────────────────────────────────────────────────────

        private float RenderStatusBar(int width, int height, ViewerStatusSnapshot status)
        {
            float barHeight = ImGui.GetFrameHeight();
            ImGui.SetNextWindowPos(new Vector2(0f, height - barHeight), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(width, barHeight), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * _scale, 2f * _scale));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, ImGuiTheme.Accent with { W = 0.85f });

            if (ImGui.Begin("##CloudScopeStatusBar", FixedPanelFlags))
            {
                StatusItem(status.HasCloud ? $"{status.PointCountText} pts" : "No point cloud");
                StatusItem(status.Mode.ToString());
                StatusItem(status.ActiveTool.ToString());
                StatusItem(status.CurrentLabel.Length > 0 ? $"Label: {status.CurrentLabel}" : "Label: —");
                StatusItem($"Instance: {status.InstanceText}");
                StatusItem(status.ProjectionText);
                StatusItem($"{status.Fps:0} fps");
            }

            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            return barHeight;
        }

        private static void StatusItem(string text)
        {
            ImGui.TextUnformatted(text);
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
        }

        // ── Expanded history (F2) ───────────────────────────────────────────────

        private void RenderHistoryWindow(int width, int height)
        {
            if (!_viewer.CommandHistoryVisible)
                return;

            bool open = true;
            ImGui.SetNextWindowSize(new Vector2(width * 0.6f, height * 0.5f), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Command history", ref open))
            {
                if (ImGui.Button("Copy all"))
                    ImGui.SetClipboardText(_session.HistoryText);

                ImGui.SameLine();
                if (ImGui.Button("Close"))
                    open = false;

                ImGui.Separator();
                PushMono();
                RenderEntries(_session.History);
                PopMono();
            }

            ImGui.End();
            _viewer.CommandHistoryVisible = open;
        }

        // ── Label registry ──────────────────────────────────────────────────────

        private void RenderLabelWindow()
        {
            if (!_viewer.LabelWindowVisible)
                return;

            bool open = true;
            ImGui.SetNextWindowSize(new Vector2(360f * _scale, 420f * _scale), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Label registry", ref open))
            {
                ImGui.TextDisabled("Map label names to LAS classification codes.");
                ImGui.Separator();

                foreach (LabelDefinition def in _viewer.LabelRegistry.Definitions)
                {
                    ImGui.PushID(def.Name);
                    ImGui.ColorButton("##swatch", new Vector4(def.Color.X, def.Color.Y, def.Color.Z, 1f),
                        ImGuiColorEditFlags.NoTooltip, new Vector2(16f * _scale, 16f * _scale));
                    ImGui.SameLine();
                    bool active = string.Equals(def.Name, _viewer.CurrentLabel, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable($"{def.Name}  (class {def.Code})", active))
                        Run($"LABEL {def.Name}");
                    ImGui.PopID();
                }

                ImGui.Separator();
                ImGui.TextDisabled($"Active instance: {_viewer.CurrentInstanceId?.ToString() ?? "none"}");
                ImGui.SetNextItemWidth(120f * _scale);
                ImGui.InputInt("Instance id", ref _instanceIdInput);
                _instanceIdInput = Math.Max(0, _instanceIdInput);
                if (ImGui.Button("Clear instance"))
                    Run("INSTANCE CLEAR");
                ImGui.SameLine();
                if (ImGui.Button("Set instance"))
                    Run($"INSTANCE {_instanceIdInput}");

                ImGui.Separator();
                ImGui.TextUnformatted("Add / update");
                ImGui.SetNextItemWidth(180f * _scale);
                ImGui.InputText("Name", ref _labelNameInput, 64);
                ImGui.SetNextItemWidth(120f * _scale);
                ImGui.InputInt("Class code", ref _labelCodeInput);
                _labelCodeInput = Math.Clamp(_labelCodeInput, 0, 255);
                if (ImGui.Button("Define") && _labelNameInput.Trim().Length > 0)
                    Run($"LABELDEF {_labelNameInput.Trim()} {_labelCodeInput}");
            }

            ImGui.End();
            _viewer.LabelWindowVisible = open;
        }

        // ── Plumbing ────────────────────────────────────────────────────────────

        private const ImGuiWindowFlags FixedPanelFlags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoBringToFrontOnFocus;

        private static void PushMono()
        {
            if (ImGuiTheme.HasMonoFont)
                ImGui.PushFont(ImGuiTheme.MonoFont);
        }

        private static void PopMono()
        {
            if (ImGuiTheme.HasMonoFont)
                ImGui.PopFont();
        }

        private void Execute(string command)
        {
            _session.Submit(command);
            _autoScrollHistory = true;
            DismissCompletions();
        }

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
