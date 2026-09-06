using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using CloudScope.Avalonia.Controls;
using CloudScope.Commands;

namespace CloudScope.Avalonia;

/// <summary>
/// The expanded command history (F2): the same transcript as the docked command window, in a
/// resizable window whose text can be selected across lines and copied — AutoCAD's text
/// window. It follows the session, so what it shows is never behind what was just run.
///
/// The window is built from the same three bands as the docked console: a quiet header strip
/// on the raised tone, the transcript in the sunken well, and a footer of hints. Each band is
/// divided from the next by one hairline; nothing inside a band is boxed.
/// </summary>
public sealed class CommandHistoryWindow : Window
{
    private readonly CommandTranscript _transcript;

    /// <summary>Raised with a command double-clicked in the history, for the command line to take.</summary>
    public event Action<string>? CommandRecalled;

    public CommandHistoryWindow(CommandLineSession session)
    {
        _transcript = new CommandTranscript(session);
        _transcript.CommandRecalled += command => CommandRecalled?.Invoke(command);

        Title = "Command history";
        Width = 760;
        Height = 460;
        MinWidth = 420;
        MinHeight = 220;
        ShowInTaskbar = false;
        Background = App.Brush("CsSurfaceDeep");

        var copy = new Button { Content = "Copy" };
        copy.Classes.Add("tool");
        ToolTip.SetTip(copy, "Copy the selection, or the whole transcript");
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
                await clipboard.SetTextAsync(_transcript.SelectedOrAllText);
        };

        var clear = new Button { Content = "Clear" };
        clear.Classes.Add("tool");
        ToolTip.SetTip(clear, "Discard the transcript");
        clear.Click += (_, _) => session.ClearHistory();

        var header = new Border
        {
            Background = App.Brush("CsSurfaceAlt"),
            BorderBrush = App.Brush("CsBorder"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4),
            [DockPanel.DockProperty] = Dock.Top,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children = { copy, clear }
            }
        };

        var footer = new Border
        {
            BorderBrush = App.Brush("CsBorder"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 5),
            [DockPanel.DockProperty] = Dock.Bottom,
            Child = new TextBlock
            {
                Classes = { "statusItem" },
                Margin = new Thickness(0),
                Text = "Double-click a command to put it back on the command line."
            }
        };

        Content = new DockPanel { Children = { header, footer, _transcript } };
    }
}
