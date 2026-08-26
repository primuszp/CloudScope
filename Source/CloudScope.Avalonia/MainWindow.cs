using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CloudScope.Avalonia.Controls;
using CloudScope.Avalonia.Hosting;
using CloudScope.Avalonia.Hosting.Input;
using CloudScope.Library;
using CloudScope.Loading;
using CloudScope.Commands;
using CloudScope.Ui;

namespace CloudScope.Avalonia;

public sealed partial class MainWindow : Window
{
    private readonly HostController _hostController = new();
    private readonly bool _useNativeMenu = OperatingSystem.IsMacOS();
    private readonly CommandLineSession _commandSession;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly List<(Button Button, string CheckState)> _toolButtons = [];
    private readonly Dictionary<string, TextBlock> _inspectorValues = new(StringComparer.Ordinal);
    private readonly ShellSettings _settings = ShellSettings.Load();

    private CommandLineControl _commandLine = null!;

    // Cached control references — resolved once after InitializeComponent
    private Menu _mainMenuControl = null!;
    private Grid _workspaceGrid = null!;
    private StackPanel _toolStripPanel = null!;
    private StackPanel _inspectorPanel = null!;
    private Grid _contentGrid = null!;
    private ContentControl _commandLineHost = null!;
    private ContentControl _viewportContainer = null!;
    private TextBlock _statusText = null!;
    private TextBlock _statusPoints = null!;
    private TextBlock _statusMode = null!;
    private TextBlock _statusLabel = null!;
    private TextBlock _statusProjection = null!;
    private TextBlock _statusFps = null!;
    private TextBlock _viewBadgeTitle = null!;
    private TextBlock _viewBadgeSubtitle = null!;

    public MainWindow()
    {
        _commandSession = new CommandLineSession(
            command => _hostController.ExecuteCommandResult(command, publishResult: false),
            () => _hostController.CommandPrompt,
            _hostController.Commands);

        InitializeComponent();
        ResolveControls();

        _hostController.StatusChanged += OnStatusChanged;
        _hostController.ViewerStateChanged += _ => Dispatcher.UIThread.Post(RefreshViewerState);

        ConfigureWindowChrome();
        BuildMenu();
        BuildToolStrip();
        BuildInspector();
        BuildCommandLine();
        _hostController.ViewerCommandOutput += OnViewerCommandOutput;

        _viewportContainer.Content = new ViewportInputHost(_hostController);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);

        _statusText.Text = _hostController.StatusText;

        // The viewer's FPS and camera state change without a command being run, so the
        // inspector and status bar are refreshed on a slow timer rather than per frame.
        _statusTimer.Tick += (_, _) => RefreshViewerState();
        _statusTimer.Start();

        Opened += (_, _) => _commandLine.FocusInput();
        Closing += (_, _) => SaveShellSettings();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ResolveControls()
    {
        _workspaceGrid      = Find<Grid>("WorkspaceGrid");
        _mainMenuControl    = Find<Menu>("MainMenuBar");
        _toolStripPanel     = Find<StackPanel>("ToolStripPanel");
        _inspectorPanel     = Find<StackPanel>("InspectorPanel");
        _contentGrid        = Find<Grid>("ContentGrid");
        _commandLineHost    = Find<ContentControl>("CommandLineHost");
        _viewportContainer  = Find<ContentControl>("ViewportHost");
        _statusText         = Find<TextBlock>("StatusText");
        _statusPoints       = Find<TextBlock>("StatusPoints");
        _statusMode         = Find<TextBlock>("StatusMode");
        _statusLabel        = Find<TextBlock>("StatusLabel");
        _statusProjection   = Find<TextBlock>("StatusProjection");
        _statusFps          = Find<TextBlock>("StatusFps");
        _viewBadgeTitle     = Find<TextBlock>("ViewBadgeTitle");
        _viewBadgeSubtitle  = Find<TextBlock>("ViewBadgeSubtitle");
    }

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} control is missing.");

    // ── Chrome ──────────────────────────────────────────────────────────────

    private void ConfigureWindowChrome()
    {
        if (!_useNativeMenu)
            return;

        // macOS: the menu belongs in the system menu bar, and the tool strip sits in a
        // unified titlebar so the window reads like a native document app.
        _mainMenuControl.IsVisible = false;
        _workspaceGrid.RowDefinitions[0].Height = new GridLength(0);

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        Find<Border>("ToolStripBorder").Padding = new Thickness(78, 0, 0, 0);
    }

    // ── Menu ────────────────────────────────────────────────────────────────

    private void BuildMenu()
    {
        IReadOnlyList<CommandMenuEntry> model = CommandMenu.Build();

        if (_useNativeMenu)
        {
            NativeMenu.SetMenu(this, BuildNativeMenu(model));
            return;
        }

        foreach (CommandMenuEntry group in model)
            _mainMenuControl.Items.Add(BuildMenuItem(group));
    }

    private MenuItem BuildMenuItem(CommandMenuEntry entry)
    {
        var item = new MenuItem { Header = entry.Header };

        if (entry.IsSubmenu)
        {
            foreach (CommandMenuEntry child in entry.Items)
            {
                if (child.IsSeparator)
                    item.Items.Add(new Separator());
                else
                    item.Items.Add(BuildMenuItem(child));
            }

            return item;
        }

        if (entry.Shortcut.Length > 0 && TryParseGesture(entry.Shortcut, out KeyGesture? gesture))
            item.InputGesture = gesture;

        item.Click += (_, _) => Activate(entry.Command);
        return item;
    }

    private NativeMenu BuildNativeMenu(IReadOnlyList<CommandMenuEntry> model)
    {
        var menu = new NativeMenu();
        foreach (CommandMenuEntry group in model)
        {
            var groupItem = new NativeMenuItem(group.Header) { Menu = new NativeMenu() };
            foreach (CommandMenuEntry child in group.Items)
                groupItem.Menu.Add(BuildNativeItem(child));
            menu.Items.Add(groupItem);
        }

        return menu;
    }

    private NativeMenuItemBase BuildNativeItem(CommandMenuEntry entry)
    {
        if (entry.IsSeparator)
            return new NativeMenuItemSeparator();

        var item = new NativeMenuItem(entry.Header);
        if (entry.IsSubmenu)
        {
            item.Menu = new NativeMenu();
            foreach (CommandMenuEntry child in entry.Items)
                item.Menu.Add(BuildNativeItem(child));
            return item;
        }

        if (entry.Shortcut.Length > 0 && TryParseGesture(entry.Shortcut, out KeyGesture? gesture))
            item.Gesture = gesture;

        item.Click += (_, _) => Activate(entry.Command);
        return item;
    }

    // "Mod" is Cmd on macOS and Ctrl elsewhere, so one menu model serves both platforms.
    private bool TryParseGesture(string shortcut, out KeyGesture? gesture)
    {
        gesture = null;
        string text = shortcut.Replace("Mod+", _useNativeMenu ? "Meta+" : "Ctrl+");

        try
        {
            gesture = KeyGesture.Parse(text);
            return true;
        }
        catch (Exception)
        {
            // A shortcut this platform cannot express is not worth failing startup over.
            return false;
        }
    }

    // ── Tool strip ──────────────────────────────────────────────────────────

    private void BuildToolStrip()
    {
        AddToolButton("⌸", "Open", "OPEN", "");
        AddToolSeparator();
        AddToolButton("✥", "Navigate", "NAVIGATE", CommandMenu.CheckStates.ModeNavigate);
        AddToolButton("✎", "Label", "LABELMODE", CommandMenu.CheckStates.ModeLabel);
        AddToolSeparator();
        AddToolButton("▭", "Box", "SELECT Box", CommandMenu.CheckStates.ToolBox);
        AddToolButton("○", "Sphere", "SELECT Sphere", CommandMenu.CheckStates.ToolSphere);
        AddToolButton("◍", "Cylinder", "SELECT Cylinder", CommandMenu.CheckStates.ToolCylinder);
        AddToolSeparator();
        AddToolButton("⤢", "Fit", "FIT", "");
        AddToolButton("✓", "Confirm", "CONFIRM", "");
        AddToolButton("✕", "Cancel", "CANCEL", "");
        AddToolSeparator();
        AddToolButton("⛶", "Extents", "ZOOM Extents", "");
    }

    private void AddToolButton(string glyph, string caption, string command, string checkState)
    {
        var button = new Button
        {
            Classes = { "tool" },
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = glyph, Classes = { "glyph" } },
                    new TextBlock { Text = caption, Classes = { "caption" } }
                }
            }
        };

        ToolTip.SetTip(button, $"{caption}  ·  {command}");
        button.Click += (_, _) => Activate(command);
        _toolStripPanel.Children.Add(button);

        if (checkState.Length > 0)
            _toolButtons.Add((button, checkState));
    }

    private void AddToolSeparator() =>
        _toolStripPanel.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(6, 4),
            [!BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("CsBorder")
        });

    // ── Inspector ───────────────────────────────────────────────────────────

    private void BuildInspector()
    {
        AddInspectorGroup("GENERAL", "File", "Points", "Filter");
        AddInspectorGroup("VIEW", "Projection", "View", "Layout", "FPS");
        AddInspectorGroup("DISPLAY", "Color by", "Point size");
        AddInspectorGroup("LABELING", "Mode", "Tool", "State", "Label", "Instance");

        var registry = new Button { Content = "Label registry...", HorizontalAlignment = HorizontalAlignment.Stretch };
        registry.Click += (_, _) => OpenLabelRegistry();
        _inspectorPanel.Children.Add(registry);
    }

    private void AddInspectorGroup(string title, params string[] rows)
    {
        _inspectorPanel.Children.Add(new TextBlock { Text = title, Classes = { "sectionTitle" } });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("104,*"), RowSpacing = 5 };
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var name = new TextBlock { Text = rows[i], Classes = { "propertyName" } };
            var value = new TextBlock { Text = "—", Classes = { "propertyValue" } };
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(name);
            grid.Children.Add(value);
            _inspectorValues[rows[i]] = value;
        }

        _inspectorPanel.Children.Add(grid);
    }

    // ── Command line ────────────────────────────────────────────────────────

    // The command window keeps the height the user dragged it to, and the input history
    // survives a restart, so the tool behaves like a workspace rather than a dialog.
    private const double CommandLineChromeHeight = 62;
    private const double CommandLineTextLineHeight = 17;

    private void BuildCommandLine()
    {
        _commandLine = new CommandLineControl(_commandSession, ExecuteCommandAsync);
        _commandLine.HistoryRequested += ToggleHistoryWindow;
        _commandLineHost.Content = _commandLine;

        _commandSession.SeedInputHistory(_settings.RecentInput);
        _workspaceGrid.RowDefinitions[3].Height = new GridLength(
            Math.Clamp(_settings.HeightInLines, 1, 20) * CommandLineTextLineHeight + CommandLineChromeHeight);
        _contentGrid.ColumnDefinitions[2].Width = new GridLength(Math.Clamp(_settings.InspectorWidth, 180, 640));
    }

    private void SaveShellSettings()
    {
        double pixels = _workspaceGrid.RowDefinitions[3].ActualHeight;
        _settings.HeightInLines = Math.Max(1, (pixels - CommandLineChromeHeight) / CommandLineTextLineHeight);
        _settings.InspectorWidth = _contentGrid.ColumnDefinitions[2].ActualWidth;
        _settings.RecentInput = _commandSession.InputHistory.ToList();
        _settings.Save();
    }

    private CommandHistoryWindow? _historyWindow;

    private void ToggleHistoryWindow()
    {
        if (_historyWindow is { } existing)
        {
            existing.Close();
            _historyWindow = null;
            return;
        }

        _historyWindow = new CommandHistoryWindow(_commandSession);
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show(this);
    }

    private LabelRegistryWindow? _labelRegistryWindow;

    private void OpenLabelRegistry()
    {
        if (_labelRegistryWindow is { } existing)
        {
            existing.Activate();
            existing.Refresh();
            return;
        }

        _labelRegistryWindow = new LabelRegistryWindow(_hostController, RunCommandFromUi);
        _labelRegistryWindow.Closed += (_, _) => _labelRegistryWindow = null;
        _labelRegistryWindow.Show(this);
    }

    /// <summary>
    /// Every menu item, toolbar button and keyboard shortcut goes through here, so the UI
    /// can only do what the user could type. Commands needing a shell-owned window are
    /// handled in <see cref="ExecuteCommandAsync"/>, which is also what typing them hits.
    /// </summary>
    private void Activate(string command) => RunCommandFromUi(command);

    private void RunCommandFromUi(string command)
    {
        _commandLine.Stage(command);
        _ = SubmitStagedCommandAsync(command);
    }

    private async Task SubmitStagedCommandAsync(string command)
    {
        await ExecuteCommandAsync(command);
        _commandLine.Clear();
        _commandLine.Refresh();
        _commandLine.FocusInput();
    }

    private async Task ExecuteCommandAsync(string command)
    {
        if (TryGetOpenPath(command, out string? path))
        {
            _commandSession.Submit(command, _ => CommandResult.End("Loading point cloud..."));
            await OpenLasAsync(path);
            RefreshViewerState();
            return;
        }

        // These two commands open windows this shell owns rather than viewer state, so they
        // are answered here instead of being forwarded to the viewer's runtime.
        if (CommandText.TryMatch(command, "HISTORY", out _))
        {
            _commandSession.Submit(command, _ =>
            {
                ToggleHistoryWindow();
                return CommandResult.End($"Command history {(_historyWindow != null ? "shown" : "hidden")}.");
            });
            return;
        }

        if (CommandText.TryMatch(command, "LABELS", out _))
        {
            _commandSession.Submit(command, _ =>
            {
                OpenLabelRegistry();
                return CommandResult.End("Label registry shown.");
            });
            return;
        }

        _commandSession.Submit(command);
        RefreshViewerState();
        await AnswerFilePromptAsync();
    }

    /// <summary>
    /// Answers a command's file prompt with the platform's file dialog. The command asked for
    /// a path; this shell can offer a nicer way to give it one, and typing the path still
    /// works, so a script is not shut out of anything the dialog can do.
    /// </summary>
    private async Task AnswerFilePromptAsync()
    {
        if (_hostController.Commands.ActiveStep is not PromptFileStep prompt)
            return;

        string? picked = prompt.Mode switch
        {
            PromptFileMode.OpenFile => await PickFileAsync(prompt.Filter, save: false),
            PromptFileMode.SaveFile => await PickFileAsync(prompt.Filter, save: true),
            _ => await PickFolderAsync()
        };

        if (picked == null)
            return;

        await ExecuteCommandAsync(picked.Contains(' ') ? $"\"{picked}\"" : picked);
    }

    private async Task<string?> PickFileAsync(string filter, bool save)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return null;

        FilePickerFileType type = filter.Length == 0
            ? FilePickerFileTypes.All
            : new FilePickerFileType(filter.ToUpperInvariant() + " files") { Patterns = [$"*.{filter}"] };

        if (save)
        {
            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save file",
                DefaultExtension = filter.Length == 0 ? null : filter,
                FileTypeChoices = [type, FilePickerFileTypes.All]
            });

            return file?.Path.LocalPath;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open file",
            AllowMultiple = false,
            FileTypeFilter = [type, FilePickerFileTypes.All]
        });

        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> PickFolderAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return null;

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select folder", AllowMultiple = false });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    // ── State refresh ───────────────────────────────────────────────────────

    private ViewerStatusSnapshot? _lastStatus;

    private void RefreshViewerState()
    {
        ViewerStatusSnapshot status = _hostController.Status;

        // FPS moves every tick, so it is written on its own; everything else is rewritten
        // only when it actually changed, instead of invalidating layout a few times a second.
        _statusFps.Text = status.Fps > 0f ? $"{status.Fps:0} fps" : "";
        SetValue("FPS", status.Fps > 0f ? $"{status.Fps:0}" : "—");

        if (_lastStatus is { } previous && status == previous with { Fps = status.Fps })
            return;

        _lastStatus = status;

        SetValue("File", status.SourceName.Length > 0 ? status.SourceName : "—");
        SetValue("Points", status.HasCloud ? status.PointCountText : "—");
        SetValue("Filter", status.Filter.Length > 0 ? status.Filter : "None");
        SetValue("Projection", status.ProjectionText);
        SetValue("View", status.ViewName);
        SetValue("Layout", status.ViewportLayout);
        SetValue("Color by", status.ColorSource.ToDisplayName());
        SetValue("Point size", $"{status.PointSize:0.0} px");
        SetValue("Mode", status.Mode.ToString());
        SetValue("Tool", status.ActiveTool.ToString());
        SetValue("State", status.InteractionState.ToString());
        SetValue("Label", status.CurrentLabel.Length > 0 ? status.CurrentLabel : "—");
        SetValue("Instance", status.InstanceText);

        _statusPoints.Text = status.HasCloud ? $"{status.PointCountText} pts" : "No point cloud";
        _statusMode.Text = $"{status.Mode} · {status.ActiveTool}";
        _statusLabel.Text = status.CurrentLabel.Length > 0
            ? $"Label: {status.CurrentLabel} ({status.InstanceText})"
            : "Label: —";
        _statusProjection.Text = status.ProjectionText;

        _viewBadgeTitle.Text = status.ViewName;
        _viewBadgeSubtitle.Text = status.ViewportLayout;

        foreach ((Button button, string checkState) in _toolButtons)
        {
            bool active = CommandMenu.IsChecked(checkState, status);
            if (active != button.Classes.Contains("active"))
            {
                if (active) button.Classes.Add("active");
                else button.Classes.Remove("active");
            }
        }
    }

    private void SetValue(string row, string value)
    {
        if (_inspectorValues.TryGetValue(row, out TextBlock? block))
            block.Text = value;
    }

    // ── Input ───────────────────────────────────────────────────────────────

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox)
            return;

        if (e.Key == Key.F2)
        {
            ToggleHistoryWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            _commandLine.Stage("HELP");
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            _ = SubmitStagedCommandAsync("");
            e.Handled = true;
            return;
        }

        // AutoCAD keeps the command line hot: typing anywhere that is not a viewer shortcut
        // starts a command instead of being swallowed by the focused control.
        ViewerKey key = AvaloniaViewerKeyMapper.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
        {
            if (IsTypingKey(e))
                _commandLine.FocusInput();

            return;
        }

        _hostController.ForwardKeyDown(key);
        e.Handled = true;
    }

    private static bool IsTypingKey(KeyEventArgs e) =>
        e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift &&
        (e.Key is >= Key.A and <= Key.Z || e.Key is >= Key.D0 and <= Key.D9);

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox)
            return;

        ViewerKey key = AvaloniaViewerKeyMapper.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyUp(key);
        e.Handled = true;
    }

    private void OnStatusChanged(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _statusText.Text = _hostController.StatusText;
            _commandSession.AddHistory(message);
            _commandLine.Refresh();
            RefreshViewerState();
        });
    }

    /// <summary>Echoes a prompt answered inside the viewport onto this shell's command line.</summary>
    private void OnViewerCommandOutput(CommandResult result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
                _commandSession.AddHistory(result.Message);
            if (result.Status == CommandStatus.Prompting && !string.IsNullOrWhiteSpace(result.Prompt))
                _commandSession.AddHistory(result.Prompt, CommandEntryKind.Prompt);

            _commandLine.Refresh();
            RefreshViewerState();
        });
    }

    private void AddHistory(string message)
    {
        _commandSession.AddHistory(message);
        _commandLine.Refresh();
    }

    // ── Loading ─────────────────────────────────────────────────────────────

    private async Task OpenLasAsync(string? path)
    {
        path ??= await PickLasPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            AddHistory("Open cancelled.");
            return;
        }

        try
        {
            var progress = new Progress<int>(pct => Dispatcher.UIThread.Post(() => _statusText.Text = $"Loading {pct}%"));
            LoadedPointCloud cloud = await Task.Run(() =>
            {
                using var reader = new LasReader(path);
                return PointCloudLoader.Load(reader, maxPoints: 0, progress: progress);
            });

            _hostController.SetPendingCloud(cloud.ToDataset(), Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            AddHistory($"Load failed: {ex.Message}");
        }
    }

    private async Task<string?> PickLasPathAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return null;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open LAS point cloud",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("LAS point clouds") { Patterns = ["*.las", "*.laz"] },
                FilePickerFileTypes.All
            ]
        });

        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private static bool TryGetOpenPath(string command, out string? path)
    {
        path = null;
        if (!CommandText.TryMatch(command, "OPEN", out string argument))
            return false;

        if (!string.IsNullOrWhiteSpace(argument))
            path = argument;
        return true;
    }
}
