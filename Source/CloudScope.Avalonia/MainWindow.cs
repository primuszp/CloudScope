using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CloudScope.Library;
using CloudScope.Loading;
using AvaloniaThickness = Avalonia.Thickness;

namespace CloudScope.Avalonia;

public sealed class MainWindow : Window
{
    private readonly HostController _hostController = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBox _commandBox = new();
    private readonly ListBox _history = new();
    private ViewportInputHost? _viewport;

    public MainWindow()
    {
        Title = "CloudScope Avalonia";
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 560;

        _hostController.StatusChanged += OnStatusChanged;
        Content = BuildLayout();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
    }

    private Control BuildLayout()
    {
        var root = new DockPanel();

        Control menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        Control commandLine = BuildCommandLine();
        DockPanel.SetDock(commandLine, Dock.Bottom);
        root.Children.Add(commandLine);

        Control statusBar = BuildStatusBar();
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        _viewport = new ViewportInputHost(_hostController);
        root.Children.Add(_viewport);

        return root;
    }

    private Menu BuildMenu()
    {
        var openLas = new MenuItem { Header = "_Open LAS..." };
        openLas.Click += async (_, _) => await OpenLasAsync();

        var status = new MenuItem { Header = "_Status" };
        status.Click += (_, _) => ExecuteCommand("STATUS");

        var reset = new MenuItem { Header = "_Reset Host" };
        reset.Click += (_, _) => ExecuteCommand("RESET");

        var exit = new MenuItem { Header = "E_xit" };
        exit.Click += (_, _) => Close();

        var navigate = new MenuItem { Header = "_Navigate" };
        navigate.Click += (_, _) => ExecuteCommand("NAVIGATE");

        var labelMode = new MenuItem { Header = "_Label Mode" };
        labelMode.Click += (_, _) => ExecuteCommand("LABELMODE");

        var box = new MenuItem { Header = "_Box" };
        box.Click += (_, _) => ExecuteCommand("SELECT B");

        var sphere = new MenuItem { Header = "_Sphere" };
        sphere.Click += (_, _) => ExecuteCommand("SELECT S");

        var cylinder = new MenuItem { Header = "_Cylinder" };
        cylinder.Click += (_, _) => ExecuteCommand("SELECT C");

        var confirm = new MenuItem { Header = "_Confirm" };
        confirm.Click += (_, _) => ExecuteCommand("CONFIRM");

        var cancel = new MenuItem { Header = "_Cancel" };
        cancel.Click += (_, _) => ExecuteCommand("CANCEL");

        return new Menu
        {
            ItemsSource = new[]
            {
                new MenuItem
                {
                    Header = "_Host",
                    ItemsSource = new[] { openLas, status, reset, exit }
                },
                new MenuItem
                {
                    Header = "_Viewer",
                    ItemsSource = new[] { navigate, labelMode, box, sphere, cylinder, confirm, cancel }
                }
            }
        };
    }

    private Control BuildCommandLine()
    {
        _commandBox.PlaceholderText = "Command: SELECT B/S/C, LABELMODE, NAVIGATE, CONFIRM, CANCEL, STATUS";
        _commandBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            ExecuteCommand(_commandBox.Text ?? "");
            _commandBox.Text = "";
            FocusViewer();
            e.Handled = true;
        };

        _history.Height = 84;

        var panel = new DockPanel
        {
            Margin = new AvaloniaThickness(8),
            LastChildFill = true
        };

        DockPanel.SetDock(_history, Dock.Top);
        panel.Children.Add(_history);
        panel.Children.Add(_commandBox);
        return panel;
    }

    private Control BuildStatusBar()
    {
        _statusText.Text = _hostController.Status;
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new AvaloniaThickness(8, 2);
        return _statusText;
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
        if (ReferenceEquals(e.Source, _commandBox))
            return;

        ViewerKey key = ViewportInputHost.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyDown(key);
        e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (ReferenceEquals(e.Source, _commandBox))
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
            _statusText.Text = _hostController.Status;
            AddHistory(message);
        });
    }

    private void AddHistory(string message)
    {
        _history.Items.Add(message);
        while (_history.Items.Count > 20)
            _history.Items.RemoveAt(0);
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
            var progress = new Progress<int>(pct => Dispatcher.UIThread.Post(() => _statusText.Text = $"Loading {pct}%"));
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
