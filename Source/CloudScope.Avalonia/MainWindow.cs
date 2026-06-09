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
    private Menu MainMenuControl => this.FindControl<Menu>("MainMenuBar")
        ?? throw new InvalidOperationException("MainMenuBar control is missing.");
    private TextBlock StatusBlock => this.FindControl<TextBlock>("StatusText")
        ?? throw new InvalidOperationException("StatusText control is missing.");
    private TextBox CommandInput => this.FindControl<TextBox>("CommandBox")
        ?? throw new InvalidOperationException("CommandBox control is missing.");
    private TextBox HistoryOutput => this.FindControl<TextBox>("HistoryText")
        ?? throw new InvalidOperationException("HistoryText control is missing.");
    private TextBlock CommandPrompt => this.FindControl<TextBlock>("CommandPromptText")
        ?? throw new InvalidOperationException("CommandPromptText control is missing.");
    private ContentControl ViewportContainer => this.FindControl<ContentControl>("ViewportHost")
        ?? throw new InvalidOperationException("ViewportHost control is missing.");

    public MainWindow()
    {
        _commandSession = new CommandLineSession(
            command => new CommandResult(
                CommandStatus.Ended,
                _hostController.ExecuteCommand(command, publishResult: false),
                _hostController.CommandPrompt),
            () => _hostController.CommandPrompt);
        InitializeComponent();
        _hostController.StatusChanged += OnStatusChanged;
        ConfigureMenuUi();
        WireMenu();
        WireToolbar();
        WireCommandBox();
        _viewport = new ViewportInputHost(_hostController);
        ViewportContainer.Content = _viewport;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        StatusBlock.Text = _hostController.Status;
        RefreshHistory();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ConfigureMenuUi()
    {
        if (!_useNativeMenu)
            return;

        MainMenuControl.IsVisible = false;
        NativeMenu.SetMenu(this, BuildNativeMenu());
    }

    private void WireMenu()
    {
        MenuItem("OpenLasMenuItem").Click += async (_, _) => await OpenLasAsync();
        MenuItem("StatusMenuItem").Click += (_, _) => StageCommand("STATUS");
        MenuItem("ResetMenuItem").Click += (_, _) => StageCommand("RESET");
        MenuItem("ExitMenuItem").Click += (_, _) => Close();
        MenuItem("NavigateMenuItem").Click += (_, _) => StageCommand("NAVIGATE");
        MenuItem("LabelModeMenuItem").Click += (_, _) => StageCommand("LABELMODE");
        MenuItem("BoxMenuItem").Click += (_, _) => StageCommand("SELECT B");
        MenuItem("SphereMenuItem").Click += (_, _) => StageCommand("SELECT S");
        MenuItem("CylinderMenuItem").Click += (_, _) => StageCommand("SELECT C");
        MenuItem("ConfirmMenuItem").Click += (_, _) => StageCommand("CONFIRM");
        MenuItem("CancelMenuItem").Click += (_, _) => StageCommand("CANCEL");
    }

    private void WireCommandBox()
    {
        CommandInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ExecuteCommand("CANCEL");
                CommandInput.Text = "";
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                CommandInput.Text = _commandSession.Recall(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                CommandInput.Text = _commandSession.Recall(1);
                e.Handled = true;
                return;
            }

            if (e.Key is not Key.Enter and not Key.Space)
                return;

            string command = CommandInput.Text ?? "";
            ExecuteCommand(command);
            CommandInput.Text = "";
            CommandInput.Focus();
            e.Handled = true;
        };
    }

    private void WireToolbar()
    {
        Button("OpenToolbarButton").Click += async (_, _) => await OpenLasAsync();
        WireCommandButton("NavigateToolbarButton", "NAVIGATE");
        WireCommandButton("BoxToolbarButton", "SELECT B");
        WireCommandButton("SphereToolbarButton", "SELECT S");
        WireCommandButton("CylinderToolbarButton", "SELECT C");
        WireCommandButton("ConfirmToolbarButton", "CONFIRM");
        WireCommandButton("CancelToolbarButton", "CANCEL");
    }

    private void WireCommandButton(string name, string command) =>
        Button(name).Click += (_, _) => StageCommand(command);

    private void StageCommand(string command)
    {
        _commandSession.Stage(command);
        CommandInput.Text = command;
        CommandInput.CaretIndex = command.Length;
        CommandInput.Focus();
    }

    private Button Button(string name) => this.FindControl<Button>(name)
        ?? throw new InvalidOperationException($"{name} control is missing.");

    private MenuItem MenuItem(string name) => this.FindControl<MenuItem>(name)
        ?? throw new InvalidOperationException($"{name} control is missing.");

    private NativeMenu BuildNativeMenu()
    {
        var menu = new NativeMenu();

        menu.Items.Add(CreateNativeGroup("Host",
        [
            NativeAction("Open LAS...", async (_, _) => await OpenLasAsync(), "Meta+O"),
            NativeAction("Status", (_, _) => StageCommand("STATUS")),
            NativeAction("Reset Host", (_, _) => StageCommand("RESET")),
            new NativeMenuItemSeparator(),
            NativeAction("Exit", (_, _) => Close(), "Meta+Q")
        ]));

        menu.Items.Add(CreateNativeGroup("Viewer",
        [
            NativeAction("Navigate", (_, _) => StageCommand("NAVIGATE")),
            NativeAction("Label Mode", (_, _) => StageCommand("LABELMODE")),
            new NativeMenuItemSeparator(),
            NativeAction("Box", (_, _) => StageCommand("SELECT B")),
            NativeAction("Sphere", (_, _) => StageCommand("SELECT S")),
            NativeAction("Cylinder", (_, _) => StageCommand("SELECT C")),
            new NativeMenuItemSeparator(),
            NativeAction("Confirm", (_, _) => StageCommand("CONFIRM"), "Meta+Enter"),
            NativeAction("Cancel", (_, _) => StageCommand("CANCEL"), "Escape")
        ]));

        return menu;
    }

    private static NativeMenuItem CreateNativeGroup(string header, params NativeMenuItemBase[] items)
    {
        var group = new NativeMenuItem(header)
        {
            Menu = new NativeMenu()
        };

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

    private void ExecuteCommand(string command)
    {
        _commandSession.Submit(command);
        RefreshHistory();
        CommandPrompt.Text = _hostController.CommandPrompt;
    }

    private void FocusViewer()
    {
        Dispatcher.UIThread.Post(() => _viewport?.FocusViewer(), DispatcherPriority.Input);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ReferenceEquals(e.Source, CommandInput))
            return;

        if (e.Key is Key.Enter or Key.Space)
        {
            ExecuteCommand("");
            e.Handled = true;
            return;
        }

        if (e.Key is Key.F1 or Key.F2 || (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            if (e.Key == Key.F1)
                StageCommand("HELP");
            CommandInput.Focus();
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
        if (ReferenceEquals(e.Source, CommandInput))
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
            StatusBlock.Text = _hostController.Status;
            _commandSession.AddHistory(message);
            RefreshHistory();
        });
    }

    private void AddHistory(string message)
    {
        _commandSession.AddHistory(message);
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        HistoryOutput.Text = string.Join(Environment.NewLine, _commandSession.History);
        HistoryOutput.CaretIndex = HistoryOutput.Text.Length;
    }

    private async Task OpenLasAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open LAS point cloud",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("LAS point clouds")
                {
                    Patterns = ["*.las", "*.laz"]
                },
                FilePickerFileTypes.All
            ]
        });

        IStorageFile? file = files.FirstOrDefault();
        string? path = file?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        AddHistory($"Command: OPEN \"{path}\"");
        AddHistory("Loading point cloud...");

        try
        {
            var progress = new Progress<int>(pct => Dispatcher.UIThread.Post(() => StatusBlock.Text = $"Loading {pct}%"));
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
}
