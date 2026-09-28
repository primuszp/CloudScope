using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using CloudScope.Commands;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// AutoCAD-style ribbon projected from <see cref="CommandRibbon"/>: a row of tabs over a row
/// of titled panels, large icon-over-caption buttons beside three-high stacks of small ones.
/// Every button submits its command string through <see cref="CommandRequested"/>, so the
/// ribbon can do nothing the command line cannot.
/// </summary>
public sealed class RibbonControl : UserControl
{
    private readonly IReadOnlyList<RibbonTab> _tabs = CommandRibbon.Build();
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Border _panelHost = new() { Classes = { "ribbonBody" } };
    private readonly Border _tabRow = new() { Classes = { "ribbonTabRow" } };
    private readonly Button _collapse = new() { Classes = { "ribbonCollapse" } };
    private readonly List<ToggleButton> _tabButtons = [];
    private readonly List<(Button Button, RibbonButton Model)> _buttons = [];
    private readonly Control[] _tabContents;

    private int _selected;
    private bool _expanded = true;

    public RibbonControl()
    {
        _tabContents = _tabs.Select(BuildTab).ToArray();

        for (int i = 0; i < _tabs.Count; i++)
        {
            int index = i;
            var tab = new ToggleButton { Content = _tabs[i].Title, Classes = { "ribbonTab" } };

            // A tab click never toggles the button off: tabs are a radio group.
            tab.Click += (_, _) => OnTabClicked(index);
            tab.DoubleTapped += (_, _) => CommandRequested?.Invoke("RIBBON Toggle");
            _tabButtons.Add(tab);
            _tabStrip.Children.Add(tab);
        }

        _collapse.Click += (_, _) => CommandRequested?.Invoke("RIBBON Toggle");
        ToolTip.SetTip(_collapse, "Minimise / expand the ribbon  ·  RIBBON");

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_collapse, Dock.Right);
        header.Children.Add(_collapse);
        header.Children.Add(_tabStrip);
        _tabRow.Child = header;

        var root = new DockPanel();
        DockPanel.SetDock(_tabRow, Dock.Top);
        root.Children.Add(_tabRow);
        root.Children.Add(_panelHost);
        Content = root;

        SelectTab(0);
        ApplyExpanded();
    }

    /// <summary>A button asked for this command line to be submitted.</summary>
    public event Action<string>? CommandRequested;

    /// <summary>Raised when the user picks another tab, so the shell can remember it.</summary>
    public event Action<string>? TabChanged;

    public string SelectedTabTitle => _tabs[_selected].Title;

    /// <summary>
    /// Left inset of the tab row. On macOS the tab row is the titlebar, and the traffic-light
    /// buttons sit in its first 78 pixels.
    /// </summary>
    public double TitleBarInset
    {
        set => _tabRow.Padding = new Thickness(value, 0, 6, 0);
    }

    public void SelectTab(string title)
    {
        int index = _tabs.ToList().FindIndex(tab => string.Equals(tab.Title, title, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            SelectTab(index);
    }

    /// <summary>Lights the buttons whose mode is active and greys those needing a cloud.</summary>
    public void UpdateState(ViewerStatusSnapshot status)
    {
        foreach ((Button button, RibbonButton model) in _buttons)
        {
            bool active = model.CheckState.Length > 0 && CommandMenu.IsChecked(model.CheckState, status);
            button.Classes.Set("active", active);
            button.IsEnabled = !model.RequiresCloud || status.HasCloud;
        }

        if (status.RibbonVisible != _expanded)
        {
            _expanded = status.RibbonVisible;
            ApplyExpanded();
        }
    }

    private void OnTabClicked(int index)
    {
        SelectTab(index);
        TabChanged?.Invoke(_tabs[index].Title);

        // A minimised ribbon opens again when a tab is picked, the way AutoCAD's does.
        if (!_expanded)
            CommandRequested?.Invoke("RIBBON On");
    }

    private void SelectTab(int index)
    {
        _selected = index;
        for (int i = 0; i < _tabButtons.Count; i++)
            _tabButtons[i].IsChecked = i == index;
        _panelHost.Child = _tabContents[index];
    }

    private void ApplyExpanded()
    {
        _panelHost.IsVisible = _expanded;
        _collapse.Content = Icons.Create(_expanded ? Icons.ChevronUp : Icons.ChevronDown, 14);
    }

    private Control BuildTab(RibbonTab tab)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < tab.Panels.Count; i++)
        {
            if (i > 0)
                row.Children.Add(new Border { Classes = { "ribbonPanelSeparator" } });
            row.Children.Add(BuildPanel(tab.Panels[i]));
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = row
        };
    }

    private Control BuildPanel(RibbonPanel panel)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        StackPanel? stack = null;

        foreach (RibbonButton model in panel.Buttons)
        {
            if (model.Size == RibbonButtonSize.Large)
            {
                stack = null;
                buttons.Children.Add(BuildButton(model));
                continue;
            }

            // Small buttons fill columns of three, top to bottom, like AutoCAD's panels.
            if (stack == null || stack.Children.Count == 3)
            {
                stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Top };
                buttons.Children.Add(stack);
            }

            stack.Children.Add(BuildButton(model));
        }

        var title = new TextBlock { Text = panel.Title, Classes = { "ribbonPanelTitle" } };
        var layout = new DockPanel();
        DockPanel.SetDock(title, Dock.Bottom);
        layout.Children.Add(title);
        layout.Children.Add(buttons);
        return new Border { Classes = { "ribbonPanel" }, Child = layout };
    }

    private Button BuildButton(RibbonButton model)
    {
        bool large = model.Size == RibbonButtonSize.Large;
        Control icon = Icons.Create(model.Icon, large ? 26 : 16);
        var caption = new TextBlock { Text = model.Caption, Classes = { "ribbonCaption" } };

        var button = new Button
        {
            Classes = { large ? "ribbonLarge" : "ribbonSmall" },
            Content = large
                ? new StackPanel { Spacing = 3, Children = { icon, caption } }
                : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, caption } }
        };

        string description = model.Tooltip.Length > 0 ? model.Tooltip : model.Caption;
        ToolTip.SetTip(button, new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = description, Classes = { "tipTitle" } },
                new TextBlock { Text = model.Command, Classes = { "tipCommand" } }
            }
        });

        button.Click += (_, _) => CommandRequested?.Invoke(model.Command);
        _buttons.Add((button, model));
        return button;
    }
}
