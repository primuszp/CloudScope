using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using CloudScope.Commands;

namespace CloudScope.Avalonia;

/// <summary>
/// Turns the menu model's shortcuts into working key bindings. Avalonia's own
/// <c>MenuItem.InputGesture</c> only draws the shortcut next to the item, so without this the
/// accelerators the menu advertises on Windows and Linux would do nothing. macOS needs none of
/// it: the native menu bar dispatches its own gestures.
/// </summary>
internal static class MenuGestures
{
    public static void Register(Window window, IReadOnlyList<CommandMenuEntry> model,
        Func<string, KeyGesture?> parse, Action<string> activate)
    {
        foreach (CommandMenuEntry entry in model)
            RegisterEntry(window, entry, parse, activate);
    }

    private static void RegisterEntry(Window window, CommandMenuEntry entry,
        Func<string, KeyGesture?> parse, Action<string> activate)
    {
        if (entry.IsSubmenu)
        {
            foreach (CommandMenuEntry child in entry.Items)
                RegisterEntry(window, child, parse, activate);
            return;
        }

        if (entry.IsSeparator || entry.Shortcut.Length == 0 || parse(entry.Shortcut) is not { } gesture)
            return;

        window.KeyBindings.Add(new KeyBinding
        {
            Gesture = gesture,
            Command = new ActivateCommand(activate, entry.Command)
        });
    }

    private sealed class ActivateCommand(Action<string> activate, string command) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => activate(command);
    }
}
