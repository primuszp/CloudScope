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
        ShowInTaskbar = false;

        var copy = new Button { Content = "Copy" };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
                await clipboard.SetTextAsync(_transcript.SelectedOrAllText);
        };

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
                    Children = { copy },
                    [DockPanel.DockProperty] = Dock.Top
                },
                _transcript
            }
        };
    }
}
