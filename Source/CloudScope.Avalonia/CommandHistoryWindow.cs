using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using CloudScope.Commands;

namespace CloudScope.Avalonia;

/// <summary>
/// The expanded command history (F2): the same lines as the docked command window, in a
/// resizable, selectable, copyable panel — AutoCAD's text window.
/// </summary>
public sealed class CommandHistoryWindow : Window
{
    private readonly CommandLineSession _session;
    private readonly SelectableTextBlock _text = new()
    {
        FontFamily = new FontFamily(global::CloudScope.Ui.UiPalette.MonoFontStack),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };

    public CommandHistoryWindow(CommandLineSession session)
    {
        _session = session;

        Title = "Command history";
        Width = 760;
        Height = 460;
        ShowInTaskbar = false;

        var copy = new Button { Content = "Copy all" };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
                await clipboard.SetTextAsync(_session.HistoryText);
        };

        var refresh = new Button { Content = "Refresh" };
        refresh.Click += (_, _) => Refresh();

        Content = new DockPanel
        {
            Margin = new global::Avalonia.Thickness(12),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new global::Avalonia.Thickness(0, 0, 0, 8),
                    Children = { copy, refresh },
                    [DockPanel.DockProperty] = Dock.Top
                },
                new ScrollViewer { Content = _text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            }
        };

        Refresh();
    }

    public void Refresh() => _text.Text = _session.HistoryText;
}
