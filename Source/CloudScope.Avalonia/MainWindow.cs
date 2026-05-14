using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CloudScope.Library;
using CloudScope.Loading;

namespace CloudScope.Avalonia;

public sealed partial class MainWindow : Window
{
    private readonly HostController _hostController = new();
    private readonly bool _useNativeMenu = OperatingSystem.IsMacOS();
    private ViewportInputHost? _viewport;
    private Menu MainMenuControl => this.FindControl<Menu>("MainMenuBar")
        ?? throw new InvalidOperationException("MainMenuBar control is missing.");
    private TextBlock StatusBlock => this.FindControl<TextBlock>("StatusText")
        ?? throw new InvalidOperationException("StatusText control is missing.");
    private TextBox CommandInput => this.FindControl<TextBox>("CommandBox")
        ?? throw new InvalidOperationException("CommandBox control is missing.");
    private ListBox HistoryView => this.FindControl<ListBox>("HistoryList")
        ?? throw new InvalidOperationException("HistoryList control is missing.");
    private ContentControl ViewportContainer => this.FindControl<ContentControl>("ViewportHost")
        ?? throw new InvalidOperationException("ViewportHost control is missing.");

    public MainWindow()
    {
        InitializeComponent();
        _hostController.StatusChanged += OnStatusChanged;
        ConfigureMenuUi();
        WireMenu();
        WireCommandBox();
        _viewport = new ViewportInputHost(_hostController);
        ViewportContainer.Content = _viewport;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        StatusBlock.Text = _hostController.Status;
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
        MenuItem("StatusMenuItem").Click += (_, _) => ExecuteCommand("STATUS");
        MenuItem("ResetMenuItem").Click += (_, _) => ExecuteCommand("RESET");
        MenuItem("ExitMenuItem").Click += (_, _) => Close();
        MenuItem("NavigateMenuItem").Click += (_, _) => ExecuteCommand("NAVIGATE");
        MenuItem("LabelModeMenuItem").Click += (_, _) => ExecuteCommand("LABELMODE");
        MenuItem("BoxMenuItem").Click += (_, _) => ExecuteCommand("SELECT B");
        MenuItem("SphereMenuItem").Click += (_, _) => ExecuteCommand("SELECT S");
        MenuItem("CylinderMenuItem").Click += (_, _) => ExecuteCommand("SELECT C");
        MenuItem("ConfirmMenuItem").Click += (_, _) => ExecuteCommand("CONFIRM");
        MenuItem("CancelMenuItem").Click += (_, _) => ExecuteCommand("CANCEL");
    }

    private void WireCommandBox()
    {
        CommandInput.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            ExecuteCommand(CommandInput.Text ?? "");
            CommandInput.Text = "";
            FocusViewer();
            e.Handled = true;
        };
    }

    private MenuItem MenuItem(string name) => this.FindControl<MenuItem>(name)
        ?? throw new InvalidOperationException($"{name} control is missing.");

    private NativeMenu BuildNativeMenu()
    {
        var menu = new NativeMenu();

        menu.Items.Add(CreateNativeGroup("Host",
        [
            NativeAction("Open LAS...", async (_, _) => await OpenLasAsync(), "Meta+O"),
            NativeAction("Status", (_, _) => ExecuteCommand("STATUS")),
            NativeAction("Reset Host", (_, _) => ExecuteCommand("RESET")),
            new NativeMenuItemSeparator(),
            NativeAction("Exit", (_, _) => Close(), "Meta+Q")
        ]));

        menu.Items.Add(CreateNativeGroup("Viewer",
        [
            NativeAction("Navigate", (_, _) => ExecuteCommand("NAVIGATE")),
            NativeAction("Label Mode", (_, _) => ExecuteCommand("LABELMODE")),
            new NativeMenuItemSeparator(),
            NativeAction("Box", (_, _) => ExecuteCommand("SELECT B")),
            NativeAction("Sphere", (_, _) => ExecuteCommand("SELECT S")),
            NativeAction("Cylinder", (_, _) => ExecuteCommand("SELECT C")),
            new NativeMenuItemSeparator(),
            NativeAction("Confirm", (_, _) => ExecuteCommand("CONFIRM"), "Meta+Enter"),
            NativeAction("Cancel", (_, _) => ExecuteCommand("CANCEL"), "Escape")
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
        if (string.IsNullOrWhiteSpace(command))
            return;

        AddHistory($"> {command.Trim()}");
        _hostController.ExecuteCommand(command);
    }

    private void FocusViewer()
    {
        Dispatcher.UIThread.Post(() => _viewport?.FocusViewer(), DispatcherPriority.Input);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ReferenceEquals(e.Source, CommandInput))
            return;

        ViewerKey key = ViewportInputHost.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyDown(key);
        e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (ReferenceEquals(e.Source, CommandInput))
            return;

        ViewerKey key = ViewportInputHost.ToViewerKey(e.Key);
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
            AddHistory(message);
        });
    }

    private void AddHistory(string message)
    {
        HistoryView.Items.Add(message);
        while (HistoryView.Items.Count > 20)
            HistoryView.Items.RemoveAt(0);
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

        AddHistory($"> OPEN {path}");
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
