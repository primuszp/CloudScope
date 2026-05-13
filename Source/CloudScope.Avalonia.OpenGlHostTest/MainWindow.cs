using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CloudScope.Library;
using CloudScope.Loading;
using AvaloniaThickness = Avalonia.Thickness;

namespace CloudScope.Avalonia.OpenGlHostTest;

public sealed class MainWindow : Window
{
    private readonly HostController _hostController = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBox _commandBox = new();
    private readonly ListBox _history = new();

    public MainWindow()
    {
        Title = "CloudScope Avalonia OpenGL Host Test";
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 560;

        _hostController.StatusChanged += OnStatusChanged;
        Content = BuildLayout();
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

        root.Children.Add(new ViewportInputHost(_hostController));

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

        return new Menu
        {
            ItemsSource = new[]
            {
                new MenuItem
                {
                    Header = "_Host",
                    ItemsSource = new[] { openLas, status, reset, exit }
                }
            }
        };
    }

    private Control BuildCommandLine()
    {
        _commandBox.PlaceholderText = "Command: STATUS, RESET, HELP";
        _commandBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            ExecuteCommand(_commandBox.Text ?? "");
            _commandBox.Text = "";
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
