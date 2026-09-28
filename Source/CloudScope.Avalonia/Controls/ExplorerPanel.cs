using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CloudScope.Commands;
using CloudScope.Labeling;

namespace CloudScope.Avalonia.Controls;

/// <summary>What an explorer row stands for.</summary>
public enum ExplorerNodeKind
{
    Clouds,
    Cloud,
    Layer,
    Labels,
    Label,
    Scene,
    Section,
    Surface,
    Polylines,
    Views,
    View
}

/// <summary>An explorer row: its kind and the name commands address it by.</summary>
public sealed record ExplorerNode(ExplorerNodeKind Kind, string Name = "")
{
    public string Key => $"{Kind}:{Name}";
}

/// <summary>
/// The scene explorer — CloudCompare's DB tree for CloudScope: every open point cloud and
/// tile-store layer, the label classes, the scene objects commands created (cross-section,
/// surface, polylines) and the saved views. It owns no state: it is rebuilt from the viewer's
/// snapshot, and every toggle, double-click and context-menu item is a command string.
/// </summary>
public sealed class ExplorerPanel : UserControl
{
    private readonly TreeView _tree = new() { Classes = { "explorerTree" } };
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private string _signature = "";
    private string _selectedKey = "";
    private bool _rebuilding;

    public ExplorerPanel()
    {
        _tree.SelectionChanged += (_, _) =>
        {
            if (_rebuilding)
                return;

            ExplorerNode? node = (_tree.SelectedItem as TreeViewItem)?.Tag as ExplorerNode;
            _selectedKey = node?.Key ?? "";
            SelectionChanged?.Invoke(node);
        };

        Content = _tree;
    }

    /// <summary>A row asked for a command to be run.</summary>
    public event Action<string>? CommandRequested;

    /// <summary>The selected row changed (null when nothing is selected).</summary>
    public event Action<ExplorerNode?>? SelectionChanged;

    public ExplorerNode? SelectedNode => (_tree.SelectedItem as TreeViewItem)?.Tag as ExplorerNode;

    /// <summary>Quotes a name so a command reads it as one argument whatever it contains.</summary>
    public static string Quote(string name) => $"\"{name}\"";

    public void Refresh(ViewerStatusSnapshot status, IReadOnlyCollection<LabelDefinition> labels, string activeLabel)
    {
        string signature = string.Join("|",
            status.SourceName, status.LoadedCount, status.VisibleCount, status.ResidentVisible, status.IsStreamed,
            string.Join(",", status.Layers.Select(l => $"{l.Name}/{l.PointCount}/{l.Visible}")),
            string.Join(",", labels.Select(l => $"{l.Name}/{l.Code}/{l.Color}")), activeLabel,
            status.CrossSection, status.SurfaceTriangles, status.SurfaceVisible, status.SurfaceProgress,
            status.PolylineCount, string.Join(",", status.NamedViews), status.LoadProgress);

        if (signature == _signature)
            return;

        _signature = signature;
        Rebuild(status, labels, activeLabel);
    }

    private void Rebuild(ViewerStatusSnapshot status, IReadOnlyCollection<LabelDefinition> labels, string activeLabel)
    {
        _rebuilding = true;
        try
        {
            RememberExpansion(_tree.Items.OfType<TreeViewItem>());
            _tree.Items.Clear();

            _tree.Items.Add(BuildClouds(status));
            _tree.Items.Add(BuildLabels(labels, activeLabel));
            _tree.Items.Add(BuildScene(status));
            _tree.Items.Add(BuildViews(status));

            TreeViewItem? selected = Find(_tree.Items.OfType<TreeViewItem>(), _selectedKey);
            _tree.SelectedItem = selected;
        }
        finally
        {
            _rebuilding = false;
        }

        // The selection may have disappeared with the object it named.
        SelectionChanged?.Invoke(SelectedNode);
    }

    // ── Groups ──────────────────────────────────────────────────────────────

    private TreeViewItem BuildClouds(ViewerStatusSnapshot status)
    {
        int count = status.IsStreamed ? status.Layers.Count : status.HasCloud ? 1 : 0;
        TreeViewItem group = Group(new ExplorerNode(ExplorerNodeKind.Clouds), "Point clouds", RibbonIcons.Cloud,
            count == 0 ? "" : count.ToString());

        if (status.IsLoading)
            group.Items.Add(Leaf(new ExplorerNode(ExplorerNodeKind.Clouds, "loading"), $"Loading… {status.LoadProgress}%",
                RibbonIcons.Import, "", dim: true));

        if (status.IsStreamed)
        {
            foreach (CloudLayerSnapshot layer in status.Layers)
            {
                var node = new ExplorerNode(ExplorerNodeKind.Layer, layer.Name);
                group.Items.Add(Leaf(node, layer.Name, RibbonIcons.Layers, FormatCount(layer.PointCount),
                    visible: layer.Visible,
                    toggle: () => $"LAYER {(layer.Visible ? "OFf" : "ON")} {Quote(layer.Name)}"));
            }
        }
        else if (status.HasCloud)
        {
            var node = new ExplorerNode(ExplorerNodeKind.Cloud, status.SourceName);
            group.Items.Add(Leaf(node, status.SourceName.Length > 0 ? status.SourceName : "Point cloud",
                RibbonIcons.Cloud, FormatCount(status.VisibleCount),
                visible: status.ResidentVisible,
                toggle: () => $"LAYER {(status.ResidentVisible ? "OFf" : "ON")} {Quote(status.SourceName)}"));
        }
        else if (!status.IsLoading)
        {
            group.Items.Add(Hint("No point cloud — Open (Ctrl+O) or drop in a tile store"));
        }

        return group;
    }

    private TreeViewItem BuildLabels(IReadOnlyCollection<LabelDefinition> labels, string activeLabel)
    {
        TreeViewItem group = Group(new ExplorerNode(ExplorerNodeKind.Labels), "Label classes", RibbonIcons.Label,
            labels.Count == 0 ? "" : labels.Count.ToString());

        foreach (LabelDefinition label in labels.OrderBy(l => l.Code))
        {
            bool active = string.Equals(label.Name, activeLabel, StringComparison.OrdinalIgnoreCase);
            var node = new ExplorerNode(ExplorerNodeKind.Label, label.Name);
            group.Items.Add(Leaf(node, label.Name, "", active ? $"active · {label.Code}" : $"{label.Code}", swatch: ToColor(label.Color),
                nameClass: active ? "active" : ""));
        }

        return group;
    }

    private TreeViewItem BuildScene(ViewerStatusSnapshot status)
    {
        TreeViewItem group = Group(new ExplorerNode(ExplorerNodeKind.Scene), "Scene objects", RibbonIcons.Section, "");

        if (status.CrossSection.Length > 0)
            group.Items.Add(Leaf(new ExplorerNode(ExplorerNodeKind.Section, "section"), "Cross-section",
                RibbonIcons.Section, status.CrossSection));

        if (status.SurfaceProgress >= 0)
            group.Items.Add(Leaf(new ExplorerNode(ExplorerNodeKind.Surface, "surface"), "Surface",
                RibbonIcons.Mesh, $"building {status.SurfaceProgress}%", dim: true));
        else if (status.SurfaceTriangles > 0)
            group.Items.Add(Leaf(new ExplorerNode(ExplorerNodeKind.Surface, "surface"), "Surface mesh",
                RibbonIcons.Mesh, $"{status.SurfaceTriangles:N0} tri",
                visible: status.SurfaceVisible,
                toggle: () => status.SurfaceVisible ? "SURFACE OFf" : "SURFACE ON"));

        if (status.PolylineCount > 0)
            group.Items.Add(Leaf(new ExplorerNode(ExplorerNodeKind.Polylines, "polylines"), "Polylines",
                RibbonIcons.Polyline, status.PolylineCount.ToString()));

        if (group.Items.Count == 0)
            group.Items.Add(Hint("Sections, surfaces and polylines appear here"));

        return group;
    }

    private TreeViewItem BuildViews(ViewerStatusSnapshot status)
    {
        TreeViewItem group = Group(new ExplorerNode(ExplorerNodeKind.Views), "Named views", Icons.Camera,
            status.NamedViews.Count == 0 ? "" : status.NamedViews.Count.ToString());

        foreach (string view in status.NamedViews)
            group.Items.Add(Leaf(new ExplorerNode(ExplorerNodeKind.View, view), view, Icons.Camera, ""));

        if (status.NamedViews.Count == 0)
            group.Items.Add(Hint("VIEW Save stores the current camera"));

        return group;
    }

    // ── Rows ────────────────────────────────────────────────────────────────

    private TreeViewItem Group(ExplorerNode node, string title, string icon, string detail)
    {
        TreeViewItem item = Leaf(node, title, icon, detail, nameClass: "group");
        item.IsExpanded = !_expanded.TryGetValue(node.Key, out bool expanded) || expanded;
        return item;
    }

    private TreeViewItem Leaf(ExplorerNode node, string title, string icon, string detail,
        bool? visible = null, Func<string>? toggle = null, Color? swatch = null, bool dim = false,
        string nameClass = "")
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Classes = { "explorerRow" } };

        Control glyph = swatch is Color color
            ? new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(3, 0),
                Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center
            }
            : Icons.Create(icon, 14);
        glyph.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(glyph);

        var name = new TextBlock { Text = title, Classes = { "explorerName" }, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim || visible == false)
            name.Classes.Add("dim");
        if (nameClass.Length > 0)
            name.Classes.Add(nameClass);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        if (detail.Length > 0)
        {
            var text = new TextBlock { Text = detail, Classes = { "explorerDetail" } };
            Grid.SetColumn(text, 2);
            row.Children.Add(text);
        }

        if (visible is bool shown && toggle != null)
        {
            var eye = new Button
            {
                Classes = { "explorerEye" },
                Content = Icons.Create(shown ? Icons.Eye : Icons.EyeOff, 14)
            };
            if (!shown) eye.Classes.Add("off");
            ToolTip.SetTip(eye, shown ? "Hide" : "Show");
            eye.Click += (_, e) =>
            {
                CommandRequested?.Invoke(toggle());
                e.Handled = true;
            };
            Grid.SetColumn(eye, 3);
            row.Children.Add(eye);
        }

        var item = new TreeViewItem { Header = row, Tag = node, ContextMenu = BuildContextMenu(node) };
        item.DoubleTapped += (_, e) =>
        {
            if (DefaultCommand(node) is { } command)
            {
                CommandRequested?.Invoke(command);
                e.Handled = true;
            }
        };
        return item;
    }

    private static TreeViewItem Hint(string text) => new()
    {
        Header = new TextBlock { Text = text, Classes = { "explorerHint" }, TextWrapping = TextWrapping.Wrap },
        Focusable = false,
        IsHitTestVisible = false
    };

    // ── Commands ────────────────────────────────────────────────────────────

    /// <summary>What a double-click on the row does.</summary>
    private static string? DefaultCommand(ExplorerNode node) => node.Kind switch
    {
        ExplorerNodeKind.Cloud or ExplorerNodeKind.Layer => "ZOOM Extents",
        ExplorerNodeKind.Label => $"LABEL {Quote(node.Name)}",
        ExplorerNodeKind.Section => "XSECTION View",
        ExplorerNodeKind.View => $"VIEW Restore {Quote(node.Name)}",
        ExplorerNodeKind.Polylines => "PEDIT",
        _ => null
    };

    /// <summary>The row's context menu — every item a command, as in the menu bar.</summary>
    public static IReadOnlyList<(string Header, string Command)> ContextCommands(ExplorerNode node) => node.Kind switch
    {
        ExplorerNodeKind.Clouds =>
        [
            ("Open LAS/LAZ...", "OPEN"), ("Import PLY...", "OPENPLY"), ("Import XYZ...", "OPENXYZ"),
            ("Import PTS...", "OPENPTS"), ("Import PTX...", "OPENPTX"), ("Import E57...", "OPENE57"),
            ("", ""), ("Open tile store...", "OPENSTORE"), ("Add tile store...", "ADDSTORE"),
            ("", ""), ("List layers", "LAYER List"), ("Store info", "STOREINFO")
        ],
        ExplorerNodeKind.Cloud =>
        [
            ("Zoom extents", "ZOOM Extents"), ("Hide", $"LAYER OFf {Quote(node.Name)}"), ("Show", $"LAYER ON {Quote(node.Name)}"),
            ("", ""), ("Color by RGB", "COLORBY Rgb"), ("Color by height", "COLORBY Height"),
            ("Color by class", "COLORBY Class"), ("Color by intensity", "COLORBY Intensity"),
            ("", ""), ("Filter...", "FILTER"), ("Thin...", "THIN"), ("Full density", "THIN 100"),
            ("Attributes", "ATTRIBUTES All"), ("", ""), ("Export PLY...", "EXPORT PlyBinary"),
            ("Export XYZ...", "EXPORT Xyz"), ("Export CSV...", "EXPORT Csv"),
            ("", ""), ("Close", $"LAYER Close {Quote(node.Name)}")
        ],
        ExplorerNodeKind.Layer =>
        [
            ("Zoom extents", "ZOOM Extents"), ("Hide", $"LAYER OFf {Quote(node.Name)}"), ("Show", $"LAYER ON {Quote(node.Name)}"),
            ("Store info", "STOREINFO"), ("", ""), ("Close layer", $"LAYER Close {Quote(node.Name)}")
        ],
        ExplorerNodeKind.Labels =>
        [
            ("Define label...", "LABELDEF"), ("Label registry", "LABELS"), ("List definitions", "LABELDEF List"),
            ("", ""), ("Save labels (JSON)", "SAVELABELS Json Default"), ("Save labels to LAS", "SAVELABELS Las"),
            ("Load labels", "LOADLABELS Default"), ("Statistics", "LABELSTAT"), ("", ""), ("Clear all labels", "CLEARLABELS")
        ],
        ExplorerNodeKind.Label =>
        [
            ("Set as active label", $"LABEL {Quote(node.Name)}"), ("Label mode", "LABELMODE"),
            ("Statistics", "LABELSTAT"), ("", ""), ("Delete definition", $"LABELDEF DElete {Quote(node.Name)}")
        ],
        ExplorerNodeKind.Scene =>
        [
            ("New cross-section...", "XSECTION"), ("Reconstruct surface...", "RECONSTRUCT"),
            ("Polyline", "PLINE"), ("3D polyline", "3DPOLY"), ("Load polylines...", "LOADPOLYLINES")
        ],
        ExplorerNodeKind.Section =>
        [
            ("Show in active viewport", "XSECTION View"), ("Flip direction", "XSECTION Flip"),
            ("Width...", "XSECTION Width"), ("", ""), ("Clear", "XSECTION CLear")
        ],
        ExplorerNodeKind.Surface =>
        [
            ("Show", "SURFACE ON"), ("Hide", "SURFACE OFf"), ("Export OBJ...", "EXPORTOBJ"), ("", ""), ("Clear", "SURFACE CLear")
        ],
        ExplorerNodeKind.Polylines =>
        [
            ("Edit polyline", "PEDIT"), ("Save polylines...", "SAVEPOLYLINES"), ("Load polylines...", "LOADPOLYLINES")
        ],
        ExplorerNodeKind.Views => [("Save current view...", "VIEW Save"), ("List views", "VIEW LIst")],
        ExplorerNodeKind.View =>
        [
            ("Restore", $"VIEW Restore {Quote(node.Name)}"), ("", ""), ("Delete", $"VIEW DElete {Quote(node.Name)}")
        ],
        _ => []
    };

    private ContextMenu? BuildContextMenu(ExplorerNode node)
    {
        IReadOnlyList<(string Header, string Command)> commands = ContextCommands(node);
        if (commands.Count == 0)
            return null;

        var items = new List<Control>();
        foreach ((string header, string command) in commands)
        {
            if (command.Length == 0)
            {
                items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = header };
            ToolTip.SetTip(item, command);
            item.Click += (_, _) => CommandRequested?.Invoke(command);
            items.Add(item);
        }

        return new ContextMenu { ItemsSource = items };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void RememberExpansion(IEnumerable<TreeViewItem> items)
    {
        foreach (TreeViewItem item in items)
        {
            if (item.Tag is ExplorerNode node && item.ItemCount > 0)
                _expanded[node.Key] = item.IsExpanded;
            RememberExpansion(item.Items.OfType<TreeViewItem>());
        }
    }

    private static TreeViewItem? Find(IEnumerable<TreeViewItem> items, string key)
    {
        if (key.Length == 0)
            return null;

        foreach (TreeViewItem item in items)
        {
            if (item.Tag is ExplorerNode node && node.Key == key)
                return item;
            if (Find(item.Items.OfType<TreeViewItem>(), key) is { } found)
                return found;
        }

        return null;
    }

    private static string FormatCount(long count) => count switch
    {
        >= 1_000_000_000 => $"{count / 1e9:0.##} G",
        >= 1_000_000 => $"{count / 1e6:0.##} M",
        >= 10_000 => $"{count / 1e3:0.#} k",
        _ => count.ToString("N0")
    };

    private static Color ToColor(OpenTK.Mathematics.Vector3 color) => Color.FromRgb(
        (byte)Math.Clamp(color.X * 255f, 0f, 255f),
        (byte)Math.Clamp(color.Y * 255f, 0f, 255f),
        (byte)Math.Clamp(color.Z * 255f, 0f, 255f));
}
