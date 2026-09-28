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
    private readonly List<(Button Button, string CheckState)> _statusToggles = [];
    private readonly ShellSettings _settings = ShellSettings.Load();

    private CommandLineControl _commandLine = null!;
    private readonly RibbonControl _ribbon = new();
    private readonly ExplorerPanel _explorer = new();
    private readonly PropertiesPanel _properties = new();

    // Cached control references — resolved once after InitializeComponent
    private Menu _mainMenuControl = null!;
    private Grid _workspaceGrid = null!;
    private Grid _contentGrid = null!;
    private Grid _paletteGrid = null!;
    private Border _paletteColumn = null!;
    private GridSplitter _paletteColumnSplitter = null!;
    private DockPanel _viewportColumn = null!;
    private ContentControl _commandLineHost = null!;
    private Border _commandLineBorder = null!;
    private GridSplitter _commandSplitter = null!;
    private ContentControl _viewportContainer = null!;
    private StackPanel _viewportControls = null!;
    private TextBlock _documentTitle = null!;
    private TextBlock _documentDetail = null!;
    private TextBlock _statusText = null!;
    private TextBlock _statusPoints = null!;
    private TextBlock _statusMode = null!;
    private TextBlock _statusLabel = null!;
    private TextBlock _statusFps = null!;
    private ProgressBar _statusProgress = null!;
    private StackPanel _statusTogglePanel = null!;

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
        BuildRibbon();
        BuildPalettes();
        BuildViewportHeader();
        BuildStatusToggles();
        BuildCommandLine();
        _hostController.ViewerCommandOutput += OnViewerCommandOutput;

        _viewportContainer.Content = new ViewportInputHost(_hostController);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnWindowTextInput, RoutingStrategies.Tunnel);

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
        _workspaceGrid         = Find<Grid>("WorkspaceGrid");
        _mainMenuControl       = Find<Menu>("MainMenuBar");
        _contentGrid           = Find<Grid>("ContentGrid");
        _paletteGrid           = Find<Grid>("PaletteGrid");
        _paletteColumn         = Find<Border>("PaletteColumn");
        _paletteColumnSplitter = Find<GridSplitter>("PaletteColumnSplitter");
        _viewportColumn        = Find<DockPanel>("ViewportColumn");
        _commandLineHost       = Find<ContentControl>("CommandLineHost");
        _commandLineBorder     = Find<Border>("CommandLineBorder");
        _commandSplitter       = Find<GridSplitter>("CommandLineSplitter");
        _viewportContainer     = Find<ContentControl>("ViewportHost");
        _viewportControls      = Find<StackPanel>("ViewportControls");
        _documentTitle         = Find<TextBlock>("DocumentTitle");
        _documentDetail        = Find<TextBlock>("DocumentDetail");
        _statusText            = Find<TextBlock>("StatusText");
        _statusPoints          = Find<TextBlock>("StatusPoints");
        _statusMode            = Find<TextBlock>("StatusMode");
        _statusLabel           = Find<TextBlock>("StatusLabel");
        _statusFps             = Find<TextBlock>("StatusFps");
        _statusProgress        = Find<ProgressBar>("StatusProgress");
        _statusTogglePanel     = Find<StackPanel>("StatusToggles");
    }

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} control is missing.");

    // ── Chrome ──────────────────────────────────────────────────────────────

    private void ConfigureWindowChrome()
    {
        if (!_useNativeMenu)
            return;

        // macOS: the menu belongs in the system menu bar, and the ribbon's tab row is the
        // unified titlebar, so the window reads like a native document app.
        _mainMenuControl.IsVisible = false;

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        _ribbon.TitleBarInset = 78;
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

        // Off macOS the drawn accelerator is all a MenuItem gives us, so the same model is
        // registered as key bindings — otherwise the menu would advertise shortcuts that do
        // nothing whenever the menu itself is closed.
        MenuGestures.Register(this, model, ParseGesture, Activate);
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
    private KeyGesture? ParseGesture(string shortcut) =>
        TryParseGesture(shortcut, out KeyGesture? gesture) ? gesture : null;

    private bool TryParseGesture(string shortcut, out KeyGesture? gesture)
    {
        gesture = null;
        string text = shortcut.Replace("Mod+", _useNativeMenu ? "Meta+" : "Ctrl+");

        // Avalonia names the number-row keys D0..D9, so a shortcut written the way it is read
        // out loud ("Mod+9") would otherwise fail to parse and silently lose its accelerator.
        if (text.Length > 1 && text[^2] == '+' && char.IsAsciiDigit(text[^1]))
            text = text[..^1] + "D" + text[^1];

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

    // ── Ribbon ──────────────────────────────────────────────────────────────

    private void BuildRibbon()
    {
        _ribbon.CommandRequested += Activate;
        _ribbon.TabChanged += tab => _settings.RibbonTab = tab;
        _ribbon.SelectTab(_settings.RibbonTab);
        Find<ContentControl>("RibbonHost").Content = _ribbon;
    }

    // ── Palettes ────────────────────────────────────────────────────────────

    private void BuildPalettes()
    {
        _explorer.CommandRequested += Activate;
        _explorer.SelectionChanged += _ => RefreshProperties(_hostController.Status);
        _properties.CommandRequested += Activate;
        Find<ContentControl>("ExplorerHost").Content = _explorer;
        Find<ContentControl>("PropertiesHost").Content = _properties;

        StackPanel explorerTools = Find<StackPanel>("ExplorerTools");
        explorerTools.Children.Add(PaletteButton(RibbonIcons.Open, "Open point cloud  ·  OPEN", "OPEN"));
        explorerTools.Children.Add(PaletteButton(RibbonIcons.Layers, "Add tile store  ·  ADDSTORE", "ADDSTORE"));
        explorerTools.Children.Add(PaletteButton(RibbonIcons.Cancel, "Close explorer  ·  EXPLORER Off", "EXPLORER Off"));

        StackPanel propertyTools = Find<StackPanel>("PropertiesTools");
        propertyTools.Children.Add(PaletteButton(RibbonIcons.Cancel, "Close properties  ·  PROPERTIES Off", "PROPERTIES Off"));

        double share = Math.Clamp(_settings.ExplorerShare, 0.15, 0.85);
        _paletteGrid.RowDefinitions[1].Height = new GridLength(share, GridUnitType.Star);
        _paletteGrid.RowDefinitions[3].Height = new GridLength(1 - share, GridUnitType.Star);
    }

    private Button PaletteButton(string icon, string tip, string command)
    {
        var button = new Button { Classes = { "paletteTool" }, Content = Icons.Create(icon, 14) };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => Activate(command);
        return button;
    }

    private bool? _palettesOnRight;
    private (bool Explorer, bool Properties)? _paletteVisibility;

    /// <summary>
    /// Shows, hides and docks the palette column to match the viewer's EXPLORER, PROPERTIES
    /// state. Hiding one palette gives its room to the other; hiding both gives it all to the
    /// viewport. The column's width survives being hidden.
    /// </summary>
    private void ProjectPalettes(ViewerStatusSnapshot status)
    {
        if (_palettesOnRight != status.PalettesOnRight)
        {
            _palettesOnRight = status.PalettesOnRight;
            double width = _contentGrid.ColumnDefinitions[status.PalettesOnRight ? 0 : 2].Width.IsAbsolute
                ? _contentGrid.ColumnDefinitions[status.PalettesOnRight ? 0 : 2].Width.Value
                : PaletteWidth();

            // Column 1 is always the splitter; the palette and the viewport trade 0 and 2.
            Grid.SetColumn(_paletteColumn, status.PalettesOnRight ? 2 : 0);
            Grid.SetColumn(_viewportColumn, status.PalettesOnRight ? 0 : 2);
            _contentGrid.ColumnDefinitions[status.PalettesOnRight ? 0 : 2].Width = GridLength.Star;
            _contentGrid.ColumnDefinitions[status.PalettesOnRight ? 2 : 0].Width = new GridLength(width);
            _paletteVisibility = null;
        }

        (bool, bool) visibility = (status.ExplorerVisible, status.PropertiesVisible);
        if (_paletteVisibility == visibility)
            return;

        // The column width is remembered before it is collapsed, not after.
        if (_paletteVisibility is { } previous && (previous.Explorer || previous.Properties))
            _paletteWidth = PaletteWidth();
        _paletteVisibility = visibility;

        bool any = status.ExplorerVisible || status.PropertiesVisible;
        _paletteColumn.IsVisible = any;
        _paletteColumnSplitter.IsVisible = any;
        ColumnDefinition paletteColumn = _contentGrid.ColumnDefinitions[status.PalettesOnRight ? 2 : 0];
        paletteColumn.Width = any ? new GridLength(_paletteWidth) : new GridLength(0);

        bool both = status.ExplorerVisible && status.PropertiesVisible;
        foreach (int row in new[] { 0, 1 })
            SetPaletteRowVisible(row, status.ExplorerVisible);
        SetPaletteRowVisible(3, status.PropertiesVisible);
        Find<GridSplitter>("PaletteSplitter").IsVisible = both;

        double share = Math.Clamp(_settings.ExplorerShare, 0.15, 0.85);
        _paletteGrid.RowDefinitions[1].Height = status.ExplorerVisible
            ? new GridLength(both ? share : 1, GridUnitType.Star) : new GridLength(0);
        _paletteGrid.RowDefinitions[3].Height = status.PropertiesVisible
            ? new GridLength(both ? 1 - share : 1, GridUnitType.Star) : new GridLength(0);
    }

    private double _paletteWidth = 300;

    private void SetPaletteRowVisible(int row, bool visible)
    {
        foreach (Control child in _paletteGrid.Children.OfType<Control>().Where(c => Grid.GetRow(c) == row))
            child.IsVisible = visible;
    }

    private double PaletteWidth()
    {
        ColumnDefinition column = _contentGrid.ColumnDefinitions[_palettesOnRight == true ? 2 : 0];
        return column.ActualWidth > 40 ? column.ActualWidth : _paletteWidth;
    }

    private void RefreshProperties(ViewerStatusSnapshot status) =>
        _properties.Refresh(_explorer.SelectedNode, status, _hostController.LabelDefinitions, _hostController.ActiveLabel);

    // ── Viewport header ─────────────────────────────────────────────────────

    private Button _viewControl = null!;
    private Button _projectionControl = null!;
    private Button _layoutControl = null!;
    private Button _colorControl = null!;

    /// <summary>
    /// AutoCAD's in-canvas viewport controls ("[Top][Parallel]"), moved into the strip over
    /// the viewport so the native render surface cannot cover them.
    /// </summary>
    private void BuildViewportHeader()
    {
        Find<ContentControl>("DocumentIcon").Content = Icons.Create(RibbonIcons.Cloud, 14);

        _viewControl = ViewportControl("View",
            ("Top", "VIEW Top"), ("Bottom", "VIEW Bottom"), ("Front", "VIEW Front"), ("Back", "VIEW BAck"),
            ("Left", "VIEW Left"), ("Right", "VIEW Right"), ("Isometric", "VIEW Isometric"),
            ("", ""), ("Zoom extents", "ZOOM Extents"), ("Save view...", "VIEW Save"));
        _projectionControl = ViewportControl("Projection",
            ("Perspective", "PROJECTION Perspective"), ("Parallel", "PROJECTION PArallel"));
        _layoutControl = ViewportControl("Viewports",
            ("Single", "VPORTS Single Top"), ("Two vertical", "VPORTS Two Vertical Top"),
            ("Two horizontal", "VPORTS Two Horizontal Top"), ("Four", "VPORTS 4 Top"),
            ("Nine", "VPORTS 9 Top"), ("", ""), ("Previous", "VPORTS PRevious Top"));
        _colorControl = ViewportControl("Color",
            ("RGB", "COLORBY Rgb"), ("Height", "COLORBY Height"), ("Classification", "COLORBY Class"),
            ("Intensity", "COLORBY Intensity"), ("Return", "COLORBY ReTurn"), ("", ""), ("Default", "COLORBY CLear"));

        _viewportControls.Children.Add(_viewControl);
        _viewportControls.Children.Add(_projectionControl);
        _viewportControls.Children.Add(_layoutControl);
        _viewportControls.Children.Add(_colorControl);
    }

    private Button ViewportControl(string tip, params (string Header, string Command)[] items)
    {
        var flyout = new MenuFlyout();
        foreach ((string header, string command) in items)
        {
            if (command.Length == 0)
            {
                flyout.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = header };
            ToolTip.SetTip(item, command);
            item.Click += (_, _) => Activate(command);
            flyout.Items.Add(item);
        }

        var button = new Button { Classes = { "viewportControl" }, Flyout = flyout };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static void SetViewportControlText(Button button, string text)
    {
        if (button.Tag as string == text)
            return;

        button.Tag = text;
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                Icons.Create(Icons.ChevronDown, 10)
            }
        };
    }

    // ── Status bar ──────────────────────────────────────────────────────────

    /// <summary>
    /// AutoCAD's status-bar toggles: each one lit while its state is on, and each click the
    /// command that flips it.
    /// </summary>
    private void BuildStatusToggles()
    {
        AddStatusToggle("ORTHO", "Ortho — lock point input to axes  ·  ORTHO (F8)", "ORTHO Toggle", CommandMenu.CheckStates.Ortho);
        AddStatusToggle("PERSP", "Perspective projection  ·  PROJECTION", "PROJECTION", CommandMenu.CheckStates.Perspective);
        AddStatusToggle("LABEL", "Label mode  ·  LABELMODE / NAVIGATE", "", CommandMenu.CheckStates.ModeLabel);
        AddStatusSeparator();
        AddStatusToggle("EXPL", "Explorer palette  ·  EXPLORER", "EXPLORER Toggle", CommandMenu.CheckStates.Explorer);
        AddStatusToggle("PROPS", "Properties palette  ·  PROPERTIES", "PROPERTIES Toggle", CommandMenu.CheckStates.Properties);
        AddStatusToggle("RIBBON", "Ribbon  ·  RIBBON", "RIBBON Toggle", CommandMenu.CheckStates.Ribbon);
        AddStatusToggle("CMD", "Command line  ·  COMMANDLINE (Ctrl+9)", "COMMANDLINE Toggle", CommandMenu.CheckStates.CommandLine);
    }

    private void AddStatusToggle(string caption, string tip, string command, string checkState)
    {
        var button = new Button { Classes = { "statusToggle" }, Content = caption };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) =>
        {
            // Label mode has two commands rather than a toggle; the button picks the one that
            // flips the current state, so what lands in the history is still a plain command.
            if (command.Length == 0)
                Activate(CommandMenu.IsChecked(checkState, _hostController.Status) ? "NAVIGATE" : "LABELMODE");
            else
                Activate(command);
        };
        _statusTogglePanel.Children.Add(button);
        _statusToggles.Add((button, checkState));
    }

    private void AddStatusSeparator() =>
        _statusTogglePanel.Children.Add(new Border { Classes = { "statusSeparator" } });

    // ── Command line ────────────────────────────────────────────────────────

    // The command window keeps the height the user dragged it to, and the input history
    // survives a restart, so the tool behaves like a workspace rather than a dialog.
    private const double CommandLineChromeHeight = 34;
    private const double CommandLineTextLineHeight = 17;

    private void BuildCommandLine()
    {
        _commandLine = new CommandLineControl(_commandSession, ExecuteCommandAsync);
        _commandLine.HistoryRequested += () => RunCommandFromUi("HISTORY");
        _commandLine.CloseRequested += () => RunCommandFromUi("COMMANDLINE Off");
        _commandLineHost.Content = _commandLine;

        _commandSession.SeedInputHistory(_settings.RecentInput);
        if (_settings.CommandLineLayoutVersion < 2)
        {
            _settings.HeightInLines = 3;
            _settings.CommandLineLayoutVersion = 2;
        }
        _workspaceGrid.RowDefinitions[3].Height = new GridLength(
            Math.Clamp(_settings.HeightInLines, 1, 20) * CommandLineTextLineHeight + CommandLineChromeHeight);
        _paletteWidth = Math.Clamp(_settings.InspectorWidth, 200, 640);
        _contentGrid.ColumnDefinitions[0].Width = new GridLength(_paletteWidth);
    }

    /// <summary>
    /// Collapses or restores the docked command window to match the viewer's COMMANDLINE
    /// state. The height the user dragged it to is remembered while it is away, so showing it
    /// again gives back the window they had rather than a default one.
    /// </summary>
    private void ProjectCommandLineVisibility(bool visible)
    {
        if (visible == _commandLineVisible)
            return;

        _commandLineVisible = visible;
        RowDefinition row = _workspaceGrid.RowDefinitions[3];

        if (visible)
        {
            _commandLineBorder.IsVisible = true;
            _commandSplitter.IsVisible = true;
            row.Height = new GridLength(_hiddenCommandLineHeight);
            _commandLine.FocusInput();
            return;
        }

        _hiddenCommandLineHeight = Math.Max(row.ActualHeight, CommandLineChromeHeight);
        _commandLineBorder.IsVisible = false;
        _commandSplitter.IsVisible = false;
        row.Height = new GridLength(0);
    }

    private bool _commandLineVisible = true;
    private double _hiddenCommandLineHeight = 70;
    private CommandLineWindow? _floatingCommandLine;

    /// <summary>
    /// Moves the one command-line control between the workspace and a floating window. It is
    /// moved rather than duplicated: two of them would be two transcripts and two inputs for
    /// one session, and the user would have to guess which one their next command went to.
    /// </summary>
    private void ProjectCommandLineFloating(bool floating)
    {
        if (floating == (_floatingCommandLine != null))
            return;

        if (floating)
        {
            var window = new CommandLineWindow();
            window.RestoreBounds(_settings.CommandLineBounds);

            // The handlers hold the window itself, not the field: docking clears the field
            // before closing it, and reading the field back here would be reading a null.
            window.Closing += (_, _) => _settings.CommandLineBounds = window.SaveBounds();

            // Closing the floating window docks it again rather than losing the command line.
            window.Closed += (_, _) =>
            {
                if (_hostController.Status.CommandLineFloating)
                    RunCommandFromUi("COMMANDLINE Dock");
            };

            _floatingCommandLine = window;
            Reparent(_commandLine, window.Host, () => window.Show(this));
            return;
        }

        CommandLineWindow? floater = _floatingCommandLine;
        _floatingCommandLine = null;
        if (floater == null)
            return;

        _settings.CommandLineBounds = floater.SaveBounds();
        Reparent(_commandLine, _commandLineHost, floater.Close);
    }

    /// <summary>
    /// Hands a control from one window to another. The two steps cannot happen in one pass:
    /// a control that joins a second window while its first one still has layout queued for
    /// it brings down the application with "InvalidateArrange on wrong LayoutManager", so the
    /// old window is left to finish its layout before the new one takes the control.
    /// </summary>
    private void Reparent(Control control, ContentControl destination, Action after)
    {
        (control.Parent as ContentControl)!.Content = null;

        Dispatcher.UIThread.Post(() =>
        {
            destination.Content = control;
            after();
            _commandLine.FocusInput();
        }, DispatcherPriority.Background);
    }

    private void SaveShellSettings()
    {
        // A hidden command window must not persist as a zero-line one.
        double pixels = _commandLineVisible ? _workspaceGrid.RowDefinitions[3].ActualHeight : _hiddenCommandLineHeight;
        _settings.HeightInLines = Math.Max(1, (pixels - CommandLineChromeHeight) / CommandLineTextLineHeight);
        ViewerStatusSnapshot status = _hostController.Status;
        _settings.InspectorWidth = status.ExplorerVisible || status.PropertiesVisible ? PaletteWidth() : _paletteWidth;
        _settings.RibbonTab = _ribbon.SelectedTabTitle;

        // Until the viewer existed the snapshot is only defaults; saving it would forget
        // the arrangement the user left.
        if (_workspaceRestored)
        {
            _settings.ExplorerVisible = status.ExplorerVisible;
            _settings.PropertiesVisible = status.PropertiesVisible;
            _settings.RibbonVisible = status.RibbonVisible;
            _settings.PalettesOnRight = status.PalettesOnRight;
        }

        if (status.ExplorerVisible && status.PropertiesVisible)
        {
            double explorer = _paletteGrid.RowDefinitions[1].ActualHeight;
            double properties = _paletteGrid.RowDefinitions[3].ActualHeight;
            if (explorer + properties > 0)
                _settings.ExplorerShare = explorer / (explorer + properties);
        }
        _settings.RecentInput = _commandSession.InputHistory.ToList();
        _settings.CommandLineFloating = _floatingCommandLine != null;
        if (_floatingCommandLine != null)
            _settings.CommandLineBounds = _floatingCommandLine.SaveBounds();

        _settings.Save();
    }

    private bool _closing;
    private CommandHistoryWindow? _historyWindow;

    /// <summary>
    /// Opens and closes this shell's windows to match the viewer's state. Closing one by hand
    /// issues the command that turns it off, so the viewer stays the only place the flag is set.
    /// </summary>
    private void ProjectViewerWindows(ViewerStatusSnapshot status)
    {
        // QUIT asks the viewer to close, and the shell that owns the window is the one that
        // can do it. Without this the command reported it was closing and nothing happened.
        if (status.CloseRequested && !_closing)
        {
            _closing = true;
            Close();
            return;
        }

        ProjectCommandLineFloating(status.CommandLineFloating);
        ProjectCommandLineVisibility(status.CommandLineVisible && !status.CommandLineFloating);

        if (status.CommandHistoryVisible && _historyWindow == null)
            ShowHistoryWindow();
        else if (!status.CommandHistoryVisible && _historyWindow != null)
            CloseHistoryWindow();

        if (status.LabelWindowVisible && _labelRegistryWindow == null)
            ShowLabelRegistry();
        else if (!status.LabelWindowVisible && _labelRegistryWindow != null)
            CloseLabelRegistry();
    }

    private void ShowHistoryWindow()
    {
        _historyWindow = new CommandHistoryWindow(_commandSession);
        _historyWindow.CommandRecalled += command => _commandLine.Stage(command);
        _historyWindow.Closed += (_, _) =>
        {
            _historyWindow = null;
            if (_hostController.Status.CommandHistoryVisible)
                RunCommandFromUi("HISTORY");
        };
        _historyWindow.Show(this);
    }

    private void CloseHistoryWindow()
    {
        CommandHistoryWindow? window = _historyWindow;
        _historyWindow = null;
        window?.Close();
    }

    private void ShowLabelRegistry()
    {
        _labelRegistryWindow = new LabelRegistryWindow(_hostController, RunCommandFromUi);
        _labelRegistryWindow.Closed += (_, _) =>
        {
            _labelRegistryWindow = null;
            if (_hostController.Status.LabelWindowVisible)
                RunCommandFromUi("LABELS");
        };
        _labelRegistryWindow.Show(this);
    }

    private void CloseLabelRegistry()
    {
        LabelRegistryWindow? window = _labelRegistryWindow;
        _labelRegistryWindow = null;
        window?.Close();
    }

    private LabelRegistryWindow? _labelRegistryWindow;

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

    /// <summary>
    /// Every submission goes to the one command interpreter. This shell adds no commands and
    /// intercepts none: what it contributes is a file dialog when a command asks for a path,
    /// and windows for the viewer state that asks to be shown.
    /// </summary>
    private async Task ExecuteCommandAsync(string command)
    {
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

    private bool _workspaceRestored;

    /// <summary>
    /// The workspace comes back the way it was left, and it does so by issuing the commands
    /// that arrange it, not by setting viewer state behind the commands' back. It waits for
    /// the embedded viewer to exist, since that is where the palette and window state lives.
    /// </summary>
    private async Task RestoreWorkspaceAsync()
    {
        if (_workspaceRestored || !_hostController.Commands.IsKnownCommand("EXPLORER"))
            return;

        _workspaceRestored = true;
        var commands = new List<string>();

        // The macOS workspace always opens with the command line in its native docked
        // position; a floating palette saved by another shell must not detach it there.
        if (_settings.CommandLineFloating && !OperatingSystem.IsMacOS())
            commands.Add("COMMANDLINE Float");
        if (_settings.PalettesOnRight)
            commands.Add("EXPLORER Right");
        if (!_settings.ExplorerVisible)
            commands.Add("EXPLORER Off");
        if (!_settings.PropertiesVisible)
            commands.Add("PROPERTIES Off");
        if (!_settings.RibbonVisible)
            commands.Add("RIBBON Off");

        foreach (string command in commands)
            await ExecuteCommandAsync(command);

        _commandLine.Refresh();
    }

    private void RefreshViewerState()
    {
        _ = RestoreWorkspaceAsync();
        ViewerStatusSnapshot status = _hostController.Status;

        // Windows and palettes follow viewer state rather than deciding for themselves.
        // LABELS, HISTORY, EXPLORER and friends are viewer commands in both shells; this one
        // draws what they asked for.
        ProjectViewerWindows(status);
        ProjectPalettes(status);

        _explorer.Refresh(status, _hostController.LabelDefinitions, _hostController.ActiveLabel);
        RefreshProperties(status);

        // FPS moves every tick, so it is written on its own.
        _statusFps.Text = status.Fps > 0f ? $"{status.Fps:0} fps" : "";

        _statusProgress.IsVisible = status.IsLoading || status.SurfaceProgress >= 0;
        _statusProgress.Value = status.IsLoading ? status.LoadProgress : Math.Max(0, status.SurfaceProgress);
        if (status.IsLoading)
            _statusText.Text = $"Loading {status.LoadProgress}%";
        else if (status.SurfaceProgress >= 0)
            _statusText.Text = $"Reconstructing surface {status.SurfaceProgress}%";
        else if (_lastStatus is { } last && (last.IsLoading || last.SurfaceProgress >= 0))
            _statusText.Text = "Ready";

        _lastStatus = status;

        _ribbon.UpdateState(status);

        _documentTitle.Text = status.HasCloud
            ? status.SourceName
            : status.IsLoading ? "Loading…" : "No point cloud";
        _documentDetail.Text = status.HasCloud
            ? $"{status.PointCountText} pts{(status.Filter.Length > 0 ? "  ·  filtered" : "")}"
            : "";
        Title = status.HasCloud ? $"{status.SourceName} — CloudScope" : "CloudScope";

        SetViewportControlText(_viewControl, status.ViewName);
        SetViewportControlText(_projectionControl, status.ProjectionText);
        SetViewportControlText(_layoutControl, status.ViewportLayout);
        SetViewportControlText(_colorControl, status.HasCloud ? status.ColorSource.ToDisplayName() : "Color");
        _colorControl.IsEnabled = status.HasCloud;

        _statusPoints.Text = status.HasCloud ? $"{status.PointCountText} pts" : "No point cloud";
        _statusMode.Text = $"{status.Mode} · {status.ActiveTool}";
        _statusLabel.Text = status.CurrentLabel.Length > 0
            ? $"Label: {status.CurrentLabel} ({status.InstanceText})"
            : "Label: —";

        foreach ((Button button, string checkState) in _statusToggles)
            button.Classes.Set("active", CommandMenu.IsChecked(checkState, status));
    }

    // ── Input ───────────────────────────────────────────────────────────────

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        _viewerTookLastKey = false;

        if (e.Source is TextBox)
            return;

        // Accelerators are the window's, not the viewer's: forwarding a modified key would
        // eat Ctrl+9 and every other binding before it could fire.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            return;

        if (e.Key == Key.F2)
        {
            RunCommandFromUi("HISTORY");
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

        // A key the viewer claims is a viewer shortcut and nothing else; anything else falls
        // through to OnWindowTextInput, which hands the character to the command line.
        ViewerKey key = AvaloniaViewerKeyMapper.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _viewerTookLastKey = true;
        _hostController.ForwardKeyDown(key);
        e.Handled = true;
    }

    /// <summary>
    /// AutoCAD keeps the command line hot: a character typed anywhere that is not a viewer
    /// shortcut starts a command. Focusing the input on KeyDown is not enough — the character
    /// that started the typing has to arrive with it, or every command would lose its first
    /// letter — so the text itself is forwarded here rather than the key.
    /// </summary>
    private void OnWindowTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is TextBox || _viewerTookLastKey || !_commandLineVisible)
            return;

        string text = e.Text ?? "";
        if (text.Length == 0 || char.IsControl(text[0]))
            return;

        _commandLine.BeginTyping(text);
        e.Handled = true;
    }

    private bool _viewerTookLastKey;

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

}
