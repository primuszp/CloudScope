using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CloudScope.Avalonia.Hosting;
using CloudScope.Avalonia.Hosting.Input;
using CloudScope.Library;
using CloudScope.Loading;
using CloudScope.Commands;

namespace CloudScope.Avalonia;

public sealed partial class MainWindow : Window
{
    private readonly HostController _hostController = new();
    private readonly bool _useNativeMenu = OperatingSystem.IsMacOS();
    private readonly CommandLineSession _commandSession;
    private ViewportInputHost? _viewport;

    // Cached control references — resolved once after InitializeComponent
    private Menu _mainMenuControl = null!;
    private TextBlock _statusBlock = null!;
    private TextBox _commandInput = null!;
    private TextBox _historyOutput = null!;
    private TextBlock _commandPrompt = null!;
    private ContentControl _viewportContainer = null!;

    public MainWindow()
    {
        _commandSession = new CommandLineSession(
            command => _hostController.ExecuteCommandResult(command, publishResult: false),
            () => _hostController.CommandPrompt);

        InitializeComponent();
        ResolveControls();

        _hostController.StatusChanged += OnStatusChanged;
        ConfigureMenuUi();
        WireMenu();
        WireToolbar();
        WireCommandBox();

        _viewport = new ViewportInputHost(_hostController);
        _viewportContainer.Content = _viewport;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);

        _statusBlock.Text = _hostController.Status;
        RefreshHistory();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ResolveControls()
    {
        _mainMenuControl  = this.FindControl<Menu>("MainMenuBar")              ?? throw new InvalidOperationException("MainMenuBar control is missing.");
        _statusBlock      = this.FindControl<TextBlock>("StatusText")           ?? throw new InvalidOperationException("StatusText control is missing.");
        _commandInput     = this.FindControl<TextBox>("CommandBox")             ?? throw new InvalidOperationException("CommandBox control is missing.");
        _historyOutput    = this.FindControl<TextBox>("HistoryText")            ?? throw new InvalidOperationException("HistoryText control is missing.");
        _commandPrompt    = this.FindControl<TextBlock>("CommandPromptText")    ?? throw new InvalidOperationException("CommandPromptText control is missing.");
        _viewportContainer = this.FindControl<ContentControl>("ViewportHost")  ?? throw new InvalidOperationException("ViewportHost control is missing.");
    }

    private void ConfigureMenuUi()
    {
        if (!_useNativeMenu)
            return;

        _mainMenuControl.IsVisible = false;
        NativeMenu.SetMenu(this, BuildNativeMenu());
    }

    private void WireMenu()
    {
        MenuItem("OpenLasMenuItem").Click     += (_, _) => RunCommandFromUi("OPEN");
        MenuItem("StatusMenuItem").Click      += (_, _) => RunCommandFromUi("STATUS");
        MenuItem("ResetMenuItem").Click       += (_, _) => RunCommandFromUi("RESET");
        MenuItem("ExitMenuItem").Click        += (_, _) => Close();
        MenuItem("NavigateMenuItem").Click    += (_, _) => RunCommandFromUi("NAVIGATE");
        MenuItem("LabelModeMenuItem").Click   += (_, _) => RunCommandFromUi("LABELMODE");
        MenuItem("BoxMenuItem").Click         += (_, _) => RunCommandFromUi("SELECT B");
        MenuItem("SphereMenuItem").Click      += (_, _) => RunCommandFromUi("SELECT S");
        MenuItem("CylinderMenuItem").Click    += (_, _) => RunCommandFromUi("SELECT C");
        MenuItem("ConfirmMenuItem").Click     += (_, _) => RunCommandFromUi("CONFIRM");
        MenuItem("CancelMenuItem").Click      += (_, _) => RunCommandFromUi("CANCEL");
    }

    private void WireCommandBox()
    {
        _commandInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _ = ExecuteCommandAsync("CANCEL");
                _commandInput.Text = "";
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                _commandInput.Text = _commandSession.Recall(-1);
                _commandInput.CaretIndex = _commandInput.Text?.Length ?? 0;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                _commandInput.Text = _commandSession.Recall(1);
                _commandInput.CaretIndex = _commandInput.Text?.Length ?? 0;
                e.Handled = true;
                return;
            }

            if (e.Key is not Key.Enter and not Key.Space)
                return;

            string command = _commandInput.Text ?? "";
            _ = ExecuteCommandAsync(command);
            _commandInput.Text = "";
            _commandInput.Focus();
            e.Handled = true;
        };
    }

    private void WireToolbar()
    {
        Button("OpenToolbarButton").Click += (_, _) => RunCommandFromUi("OPEN");
        WireCommandButton("NavigateToolbarButton", "NAVIGATE");
        WireCommandButton("BoxToolbarButton",      "SELECT B");
        WireCommandButton("SphereToolbarButton",   "SELECT S");
        WireCommandButton("CylinderToolbarButton", "SELECT C");
        WireCommandButton("ConfirmToolbarButton",  "CONFIRM");
        WireCommandButton("CancelToolbarButton",   "CANCEL");
    }

    private void WireCommandButton(string name, string command) =>
        Button(name).Click += (_, _) => RunCommandFromUi(command);

    private void StageCommand(string command)
    {
        _commandSession.Stage(command);
        _commandInput.Text = command;
        _commandInput.CaretIndex = command.Length;
        _commandInput.Focus();
    }

    private void RunCommandFromUi(string command)
    {
        StageCommand(command);
        _ = SubmitStagedCommandAsync(command);
    }

    private async Task SubmitStagedCommandAsync(string command)
    {
        await ExecuteCommandAsync(command);
        _commandInput.Text = "";
        _commandInput.Focus();
    }

    private Button Button(string name) =>
        this.FindControl<Button>(name)
        ?? throw new InvalidOperationException($"{name} control is missing.");

    private MenuItem MenuItem(string name) =>
        this.FindControl<MenuItem>(name)
        ?? throw new InvalidOperationException($"{name} control is missing.");

    private NativeMenu BuildNativeMenu()
    {
        var menu = new NativeMenu();

        menu.Items.Add(CreateNativeGroup("Host",
        [
            NativeAction("Open LAS...", (_, _) => RunCommandFromUi("OPEN"), "Meta+O"),
            NativeAction("Status",      (_, _) => RunCommandFromUi("STATUS")),
            NativeAction("Reset Host",  (_, _) => RunCommandFromUi("RESET")),
            new NativeMenuItemSeparator(),
            NativeAction("Exit",        (_, _) => Close(), "Meta+Q")
        ]));

        menu.Items.Add(CreateNativeGroup("Viewer",
        [
            NativeAction("Navigate",   (_, _) => RunCommandFromUi("NAVIGATE")),
            NativeAction("Label Mode", (_, _) => RunCommandFromUi("LABELMODE")),
            new NativeMenuItemSeparator(),
            NativeAction("Box",      (_, _) => RunCommandFromUi("SELECT B")),
            NativeAction("Sphere",   (_, _) => RunCommandFromUi("SELECT S")),
            NativeAction("Cylinder", (_, _) => RunCommandFromUi("SELECT C")),
            new NativeMenuItemSeparator(),
            NativeAction("Confirm", (_, _) => RunCommandFromUi("CONFIRM"), "Meta+Enter"),
            NativeAction("Cancel",  (_, _) => RunCommandFromUi("CANCEL"),  "Escape")
        ]));

        return menu;
    }

    private static NativeMenuItem CreateNativeGroup(string header, params NativeMenuItemBase[] items)
    {
        var group = new NativeMenuItem(header) { Menu = new NativeMenu() };
        foreach (NativeMenuItemBase item in items)
            group.Menu.Add(item);
        return group;
    }

    private static NativeMenuItem NativeAction(string header, EventHandler handler, string? gesture = null)
    {
        var item = new NativeMenuItem(header);
        item.Click += handler;
        if (!string.IsNullOrWhiteSpace(gesture))
            item.Gesture = KeyGesture.Parse(gesture);
        return item;
    }

    private async Task ExecuteCommandAsync(string command)
    {
        if (TryGetOpenPath(command, out string? path))
        {
            _commandSession.Submit(command, _ => CommandResult.End("Loading point cloud..."));
            RefreshHistory();
            _commandPrompt.Text = _hostController.CommandPrompt;
            await OpenLasAsync(path);
            return;
        }

        _commandSession.Submit(command);
        RefreshHistory();
        _commandPrompt.Text = _hostController.CommandPrompt;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ReferenceEquals(e.Source, _commandInput))
            return;

        if (e.Key is Key.Enter or Key.Space)
        {
            _ = ExecuteCommandAsync("");
            e.Handled = true;
            return;
        }

        if (e.Key is Key.F1 or Key.F2 || (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            if (e.Key == Key.F1)
                StageCommand("HELP");
            _commandInput.Focus();
            e.Handled = true;
            return;
        }

        ViewerKey key = AvaloniaViewerKeyMapper.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyDown(key);
        e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (ReferenceEquals(e.Source, _commandInput))
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
            _statusBlock.Text = _hostController.Status;
            _commandSession.AddHistory(message);
            RefreshHistory();
            _commandPrompt.Text = _hostController.CommandPrompt;
        });
    }

    private void AddHistory(string message)
    {
        _commandSession.AddHistory(message);
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        _historyOutput.Text = string.Join(Environment.NewLine, _commandSession.History);
        _historyOutput.CaretIndex = _historyOutput.Text.Length;
    }

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
            var progress = new Progress<int>(pct => Dispatcher.UIThread.Post(() => _statusBlock.Text = $"Loading {pct}%"));
            LoadedPointCloud cloud = await Task.Run(() =>
            {
                using var reader = new LasReader(path);
                return PointCloudLoader.Load(reader, 5_000_000, progress);
            });

            PointCloudLoader.PrepareProgressiveSubsample(cloud.Points, cloud.LoadedCount);
            _hostController.SetPendingCloud(cloud.Points, (int)cloud.LoadedCount, cloud.Radius, Path.GetFileName(path));
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
