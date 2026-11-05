using Avalonia.Controls;
using Avalonia.Input;
using CloudScope.Commands;
using CloudScope.Ui;

namespace CloudScope.Avalonia;

/// <summary>Adapts the shared command model to native or Avalonia menus and accelerators.</summary>
internal sealed class WorkspaceMenu(Window owner, Menu menuControl, bool native, Action<string> activate)
{
    public void Build()
    {
        IReadOnlyList<CommandMenuEntry> model = CommandMenu.Build();

        if (native)
        {
            NativeMenu.SetMenu(owner, BuildNativeMenu(model));
            return;
        }

        foreach (CommandMenuEntry group in model)
            menuControl.Items.Add(BuildMenuItem(group));

        // Off macOS the drawn accelerator is all a MenuItem gives us, so the same model is
        // registered as key bindings — otherwise the menu would advertise shortcuts that do
        // nothing whenever the menu itself is closed.
        MenuGestures.Register(owner, model, ParseGesture, activate);
    }

    private MenuItem BuildMenuItem(CommandMenuEntry entry)
    {
        var item = new MenuItem { Header = entry.Header };

        if (entry.IsSubmenu)
        {
            foreach (CommandMenuEntry child in entry.Items)
            {
                if (child.IsSeparator)
                    item.Items.Add(new Separator());
                else
                    item.Items.Add(BuildMenuItem(child));
            }

            return item;
        }

        if (entry.Shortcut.Length > 0 && TryParseGesture(entry.Shortcut, out KeyGesture? gesture))
            item.InputGesture = gesture;

        item.Click += (_, _) => activate(entry.Command);
        return item;
    }

    private NativeMenu BuildNativeMenu(IReadOnlyList<CommandMenuEntry> model)
    {
        var menu = new NativeMenu();
        foreach (CommandMenuEntry group in model)
        {
            var groupItem = new NativeMenuItem(group.Header) { Menu = new NativeMenu() };
            foreach (CommandMenuEntry child in group.Items)
                groupItem.Menu.Add(BuildNativeItem(child));
            menu.Items.Add(groupItem);
        }

        return menu;
    }

    private NativeMenuItemBase BuildNativeItem(CommandMenuEntry entry)
    {
        if (entry.IsSeparator)
            return new NativeMenuItemSeparator();

        var item = new NativeMenuItem(entry.Header);
        if (entry.IsSubmenu)
        {
            item.Menu = new NativeMenu();
            foreach (CommandMenuEntry child in entry.Items)
                item.Menu.Add(BuildNativeItem(child));
            return item;
        }

        if (entry.Shortcut.Length > 0 && TryParseGesture(entry.Shortcut, out KeyGesture? gesture))
            item.Gesture = gesture;

        item.Click += (_, _) => activate(entry.Command);
        return item;
    }

    // "Mod" is Cmd on macOS and Ctrl elsewhere, so one menu model serves both platforms.
    private KeyGesture? ParseGesture(string shortcut) =>
        TryParseGesture(shortcut, out KeyGesture? gesture) ? gesture : null;

    private bool TryParseGesture(string shortcut, out KeyGesture? gesture)
    {
        gesture = null;
        string text = shortcut.Replace("Mod+", native ? "Meta+" : "Ctrl+");

        // Avalonia names the number-row keys D0..D9, so a shortcut written the way it is read
        // out loud ("Mod+9") would otherwise fail to parse and silently lose its accelerator.
        if (text.Length > 1 && text[^2] == '+' && char.IsAsciiDigit(text[^1]))
            text = text[..^1] + "D" + text[^1];

        try
        {
            gesture = KeyGesture.Parse(text);
            return true;
        }
        catch (Exception)
        {
            // A shortcut this platform cannot express is not worth failing startup over.
            return false;
        }
    }

}
