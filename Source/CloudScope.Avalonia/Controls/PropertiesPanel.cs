using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Loading;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// AutoCAD's Properties palette for the explorer's selection: grouped name/value rows, some
/// of them editable. An edit never touches the viewer: it becomes the command a user would
/// have typed (<c>COLORBY Height</c>, <c>POINTSIZE 3</c>, <c>LAYER OFf "scan.las"</c>) and is
/// submitted through <see cref="CommandRequested"/>, so the command line stays the audit trail.
/// With nothing selected it shows the workspace — view, display and labeling state.
/// </summary>
public sealed class PropertiesPanel : UserControl
{
    private readonly StackPanel _rows = new() { Classes = { "propertyList" } };
    private readonly DispatcherTimer _pointSizeCommit = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private string _signature = "";
    private double _pendingPointSize;
    private DateTime _holdRebuildUntil;

    public PropertiesPanel()
    {
        _pointSizeCommit.Tick += (_, _) =>
        {
            _pointSizeCommit.Stop();
            Run(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"POINTSIZE {_pendingPointSize:0.#}"));
        };

        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _rows
        };
    }

    public event Action<string>? CommandRequested;

    public void Refresh(ExplorerNode? node, ViewerStatusSnapshot status,
        IReadOnlyCollection<LabelDefinition> labels, string activeLabel)
    {
        // A slider being dragged must not be torn down under the pointer by the rebuild its
        // own command causes; the palette catches up once the drag settles.
        if (DateTime.UtcNow < _holdRebuildUntil)
            return;

        string signature = string.Join("|", node?.Key, status.SourceName, status.SourcePath, status.LoadedCount,
            status.VisibleCount, status.ResidentVisible, status.ColorSource, status.PointSize, status.Filter,
            status.KeepPercentage, status.IsPerspective, status.ViewName, status.ViewportLayout, status.Mode,
            status.ActiveTool, status.InteractionState, status.CurrentLabel, status.InstanceText, status.OrthoMode,
            status.CrossSection, status.SurfaceTriangles, status.SurfaceVisible, status.PolylineCount,
            status.LabelledPoints, status.HasColor, status.HasCloud,
            string.Join(",", status.Layers.Select(l => $"{l.Name}/{l.PointCount}/{l.Visible}")),
            string.Join(",", labels.Select(l => $"{l.Name}/{l.Code}/{l.Color}")), activeLabel);

        if (signature == _signature)
            return;

        _signature = signature;
        _rows.Children.Clear();

        switch (node?.Kind)
        {
            case ExplorerNodeKind.Cloud:
                BuildCloud(status);
                break;
            case ExplorerNodeKind.Layer:
                BuildLayer(node.Name, status);
                break;
            case ExplorerNodeKind.Label:
                BuildLabel(node.Name, labels, activeLabel);
                break;
            case ExplorerNodeKind.Section:
                BuildSection(status);
                break;
            case ExplorerNodeKind.Surface:
                BuildSurface(status);
                break;
            case ExplorerNodeKind.Polylines:
                BuildPolylines(status);
                break;
            case ExplorerNodeKind.View:
                BuildView(node.Name);
                break;
            default:
                BuildWorkspace(status, labels);
                break;
        }
    }

    // ── Pages ───────────────────────────────────────────────────────────────

    private void BuildWorkspace(ViewerStatusSnapshot status, IReadOnlyCollection<LabelDefinition> labels)
    {
        Header("Workspace", RibbonIcons.Properties);

        Section("View");
        Row("Projection", Choice(status.ProjectionText,
            ("Perspective", "PROJECTION Perspective"), ("Parallel", "PROJECTION PArallel")));
        Row("View", Choice(status.ViewName,
            ("Top", "VIEW Top"), ("Bottom", "VIEW Bottom"), ("Front", "VIEW Front"), ("Back", "VIEW BAck"),
            ("Left", "VIEW Left"), ("Right", "VIEW Right"), ("Isometric", "VIEW Isometric")));
        Row("Viewports", Choice(status.ViewportLayout, LayoutChoices));

        Section("Display");
        Row("Color by", ColorChoice(status));
        Row("Point size", PointSizeEditor(status.PointSize));
        Row("Ortho", Toggle(status.OrthoMode, on => on ? "ORTHO ON" : "ORTHO OFF"));

        Section("Labeling");
        Row("Mode", Choice(status.Mode.ToString(), ("Navigate", "NAVIGATE"), ("Label", "LABELMODE")));
        Row("Tool", Choice(status.ActiveTool.ToString(),
            ("Box", "SELECT Box"), ("Sphere", "SELECT Sphere"), ("Cylinder", "SELECT Cylinder")));
        Row("State", Value(status.InteractionState.ToString()));
        Row("Active label", Choice(status.CurrentLabel.Length > 0 ? status.CurrentLabel : "—",
            labels.OrderBy(l => l.Code).Select(l => (l.Name, $"LABEL {ExplorerPanel.Quote(l.Name)}")).ToArray()));
        Row("Instance", InstanceEditor(status));
        Row("Labelled", Value(status.LabelledPoints > 0 ? $"{status.LabelledPoints:N0} points" : "none"));

        if (!status.HasCloud)
        {
            Section("Getting started");
            Actions(("Open…", "OPEN"), ("Tile store…", "OPENSTORE"), ("Help", "HELP"));
        }
    }

    private void BuildCloud(ViewerStatusSnapshot status)
    {
        Header(status.SourceName, RibbonIcons.Cloud);

        Section("General");
        Row("Name", Value(status.SourceName));
        Row("Path", Value(status.SourcePath.Length > 0 ? status.SourcePath : "—", tip: status.SourcePath));
        Row("Format", Value(Path.GetExtension(status.SourceName).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "—"));
        Row("Points", Value($"{status.LoadedCount:N0}"));
        Row("Displayed", Value($"{status.VisibleCount:N0}"));
        Row("Visible", Toggle(status.ResidentVisible,
            on => $"LAYER {(on ? "ON" : "OFf")} {ExplorerPanel.Quote(status.SourceName)}"));

        Section("Display");
        Row("Color by", ColorChoice(status));
        Row("RGB colour", Value(status.HasColor ? "yes" : "no"));
        Row("Point size", PointSizeEditor(status.PointSize));

        Section("Density");
        Row("Filter", Value(status.Filter.Length > 0 ? status.Filter : "none"));
        Row("Thinning", Choice($"{status.KeepPercentage:0.#} %",
            ("100 % (full)", "THIN 100"), ("50 %", "THIN 50"), ("25 %", "THIN 25"),
            ("10 %", "THIN 10"), ("5 %", "THIN 5"), ("1 %", "THIN 1")));

        Actions(("Zoom", "ZOOM Extents"), ("Filter…", "FILTER"), ("Attributes", "ATTRIBUTES All"));
        Actions(("Export…", "EXPORT PlyBinary"), ("Close", $"LAYER Close {ExplorerPanel.Quote(status.SourceName)}"));
    }

    private void BuildLayer(string name, ViewerStatusSnapshot status)
    {
        CloudLayerSnapshot? layer = status.Layers.FirstOrDefault(l => l.Name == name);
        Header(name, RibbonIcons.Layers);
        if (layer == null)
            return;

        Section("Tile store layer");
        Row("Name", Value(layer.Name));
        Row("Points", Value($"{layer.PointCount:N0}"));
        Row("Visible", Toggle(layer.Visible, on => $"LAYER {(on ? "ON" : "OFf")} {ExplorerPanel.Quote(layer.Name)}"));
        Row("Point size", PointSizeEditor(status.PointSize));

        Actions(("Zoom", "ZOOM Extents"), ("Store info", "STOREINFO"), ("Close", $"LAYER Close {ExplorerPanel.Quote(layer.Name)}"));
    }

    private void BuildLabel(string name, IReadOnlyCollection<LabelDefinition> labels, string activeLabel)
    {
        Header(name, RibbonIcons.Label);
        if (labels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) is not { Name.Length: > 0 } label)
            return;

        bool active = string.Equals(label.Name, activeLabel, StringComparison.OrdinalIgnoreCase);
        Section("Label class");
        Row("Name", Value(label.Name));
        Row("LAS class", Value(label.Code.ToString()));
        Row("Colour", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromRgb(
                        (byte)Math.Clamp(label.Color.X * 255f, 0, 255),
                        (byte)Math.Clamp(label.Color.Y * 255f, 0, 255),
                        (byte)Math.Clamp(label.Color.Z * 255f, 0, 255)))
                },
                Value(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{label.Color.X:0.##}, {label.Color.Y:0.##}, {label.Color.Z:0.##}"))
            }
        });
        Row("Active", Value(active ? "yes — new selections get this label" : "no"));

        Actions(("Set active", $"LABEL {ExplorerPanel.Quote(label.Name)}"), ("Label mode", "LABELMODE"),
            ("Statistics", "LABELSTAT"));
        Actions(("Delete definition", $"LABELDEF DElete {ExplorerPanel.Quote(label.Name)}"));
    }

    private void BuildSection(ViewerStatusSnapshot status)
    {
        Header("Cross-section", RibbonIcons.Section);
        Section("Section");
        Row("Definition", Value(status.CrossSection.Length > 0 ? status.CrossSection : "none"));
        Actions(("Show", "XSECTION View"), ("Flip", "XSECTION Flip"), ("Width…", "XSECTION Width"));
        Actions(("Clear", "XSECTION CLear"));
    }

    private void BuildSurface(ViewerStatusSnapshot status)
    {
        Header("Surface mesh", RibbonIcons.Mesh);
        Section("Surface");
        Row("Triangles", Value(status.SurfaceProgress >= 0 ? $"building {status.SurfaceProgress}%" : $"{status.SurfaceTriangles:N0}"));
        Row("Visible", Toggle(status.SurfaceVisible, on => on ? "SURFACE ON" : "SURFACE OFf"));
        Actions(("Export OBJ…", "EXPORTOBJ"), ("Rebuild…", "RECONSTRUCT"), ("Clear", "SURFACE CLear"));
    }

    private void BuildPolylines(ViewerStatusSnapshot status)
    {
        Header("Polylines", RibbonIcons.Polyline);
        Section("Drawing");
        Row("Count", Value(status.PolylineCount.ToString()));
        Row("Ortho", Toggle(status.OrthoMode, on => on ? "ORTHO ON" : "ORTHO OFF"));
        Actions(("Edit", "PEDIT"), ("Save…", "SAVEPOLYLINES"), ("Load…", "LOADPOLYLINES"));
    }

    private void BuildView(string name)
    {
        Header(name, Icons.Camera);
        Section("Named view");
        Row("Name", Value(name));
        Actions(("Restore", $"VIEW Restore {ExplorerPanel.Quote(name)}"), ("Delete", $"VIEW DElete {ExplorerPanel.Quote(name)}"));
    }

    // ── Building blocks ─────────────────────────────────────────────────────

    private static readonly (string, string)[] LayoutChoices =
    [
        ("Single", "VPORTS Single Top"), ("Two vertical", "VPORTS Two Vertical Top"),
        ("Two horizontal", "VPORTS Two Horizontal Top"), ("Four", "VPORTS 4 Top"),
        ("Nine", "VPORTS 9 Top"), ("Previous", "VPORTS PRevious Top")
    ];

    private Control ColorChoice(ViewerStatusSnapshot status)
    {
        Control choice = Choice(status.ColorSource.ToDisplayName(),
            ("RGB", "COLORBY Rgb"), ("Height", "COLORBY Height"), ("Classification", "COLORBY Class"),
            ("Intensity", "COLORBY Intensity"), ("Return", "COLORBY ReTurn"), ("Default", "COLORBY CLear"));
        choice.IsEnabled = status.HasCloud;
        return choice;
    }

    private void Header(string title, string icon)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Classes = { "propertyHeader" },
            Children =
            {
                Icons.Create(icon, 16),
                new TextBlock { Text = title, Classes = { "propertyHeaderText" }, TextTrimming = TextTrimming.CharacterEllipsis }
            }
        };
        _rows.Children.Add(header);
    }

    private void Section(string title) =>
        _rows.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Classes = { "propertySection" } });

    private void Row(string name, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*"), Classes = { "propertyRow" } };
        var label = new TextBlock { Text = name, Classes = { "propertyName" }, VerticalAlignment = VerticalAlignment.Center };
        editor.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(label);
        grid.Children.Add(editor);
        _rows.Children.Add(grid);
    }

    private static TextBlock Value(string text, string tip = "")
    {
        var block = new TextBlock { Text = text, Classes = { "propertyValue" } };
        if (tip.Length > 0)
            ToolTip.SetTip(block, tip);
        return block;
    }

    /// <summary>A value that opens a list of commands — AutoCAD's property drop-down.</summary>
    private Control Choice(string current, params (string Label, string Command)[] options)
    {
        var flyout = new MenuFlyout();
        foreach ((string label, string command) in options)
        {
            var item = new MenuItem { Header = label };
            ToolTip.SetTip(item, command);
            item.Click += (_, _) => Run(command);
            flyout.Items.Add(item);
        }

        var content = new DockPanel();
        Control chevron = Icons.Create(Icons.ChevronDown, 12);
        DockPanel.SetDock(chevron, Dock.Right);
        content.Children.Add(chevron);
        content.Children.Add(new TextBlock { Text = current, TextTrimming = TextTrimming.CharacterEllipsis });

        return new Button
        {
            Classes = { "propertyChoice" },
            Content = content,
            Flyout = flyout,
            IsEnabled = options.Length > 0
        };
    }

    private CheckBox Toggle(bool value, Func<bool, string> command)
    {
        var box = new CheckBox { IsChecked = value, Classes = { "propertyToggle" } };
        box.IsCheckedChanged += (_, _) => Run(command(box.IsChecked == true));
        return box;
    }

    private Control PointSizeEditor(float size)
    {
        var value = new TextBlock
        {
            Text = $"{size:0.#} px",
            Classes = { "propertyValue" },
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var slider = new Slider
        {
            Minimum = 0.5,
            Maximum = 12,
            Value = size,
            SmallChange = 0.5,
            LargeChange = 1,
            TickFrequency = 0.5,
            IsSnapToTickEnabled = true,
            Classes = { "propertySlider" }
        };

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
                return;
            value.Text = $"{slider.Value:0.#} px";
            _pendingPointSize = slider.Value;
            _holdRebuildUntil = DateTime.UtcNow.AddMilliseconds(700);
            _pointSizeCommit.Stop();
            _pointSizeCommit.Start();
        };

        var layout = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        layout.Children.Add(value);
        layout.Children.Add(slider);
        return layout;
    }

    private Control InstanceEditor(ViewerStatusSnapshot status)
    {
        var box = new TextBox
        {
            Text = status.CurrentInstanceId?.ToString() ?? "",
            PlaceholderText = "none",
            Classes = { "propertyInput" }
        };

        void Commit()
        {
            string text = box.Text?.Trim() ?? "";
            string current = status.CurrentInstanceId?.ToString() ?? "";
            if (text == current)
                return;
            Run(text.Length == 0 ? "INSTANCE CLear" : int.TryParse(text, out int id) && id >= 0 ? $"INSTANCE {id}" : "INSTANCE");
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            Commit();
            e.Handled = true;
        };
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    private void Actions(params (string Label, string Command)[] actions)
    {
        var panel = new WrapPanel { Classes = { "propertyActions" } };
        foreach ((string label, string command) in actions)
        {
            var button = new Button { Content = label, Classes = { "propertyAction" } };
            ToolTip.SetTip(button, command);
            button.Click += (_, _) => Run(command);
            panel.Children.Add(button);
        }

        _rows.Children.Add(panel);
    }

    private void Run(string command) => CommandRequested?.Invoke(command);
}
