namespace CloudScope.Commands;

/// <summary>
/// One entry of the application menu. Every entry is either a separator, a submenu, or a
/// command string — menus never call viewer APIs directly, so what a menu does is exactly
/// what the user could type.
/// </summary>
public sealed record CommandMenuEntry
{
    public static readonly CommandMenuEntry Separator = new() { IsSeparator = true };

    private CommandMenuEntry() { }

    public string Header { get; private init; } = "";

    /// <summary>Command text to submit, empty for submenus and separators.</summary>
    public string Command { get; private init; } = "";

    /// <summary>
    /// Portable shortcut, written with "Mod" for the platform's primary modifier
    /// (Cmd on macOS, Ctrl elsewhere). Empty when the entry has no shortcut.
    /// </summary>
    public string Shortcut { get; private init; } = "";

    public IReadOnlyList<CommandMenuEntry> Items { get; private init; } = [];

    public bool IsSeparator { get; private init; }
    public bool IsSubmenu => Items.Count > 0;

    /// <summary>
    /// Name of the snapshot state this entry reflects, used to draw a check mark
    /// (see <see cref="CommandMenu.IsChecked"/>). Empty for plain actions.
    /// </summary>
    public string CheckState { get; private init; } = "";

    /// <summary>True for entries a shell should hide when it cannot perform them.</summary>
    public bool RequiresCloud { get; private init; }

    public static CommandMenuEntry Action(string header, string command, string shortcut = "",
        string checkState = "", bool requiresCloud = false) =>
        new() { Header = header, Command = command, Shortcut = shortcut, CheckState = checkState, RequiresCloud = requiresCloud };

    public static CommandMenuEntry Submenu(string header, params CommandMenuEntry[] items) =>
        new() { Header = header, Items = items };
}

/// <summary>
/// The single definition of CloudScope's menu tree. The Avalonia shell projects it into a
/// macOS <c>NativeMenu</c> or a Windows/Linux <c>Menu</c>, and the GameWindow shell renders it
/// as an ImGui main menu bar, so the two can no longer drift apart.
/// </summary>
public static class CommandMenu
{
    public static IReadOnlyList<CommandMenuEntry> Build() =>
    [
        CommandMenuEntry.Submenu("File",
            CommandMenuEntry.Action("Open point cloud...", "OPEN", "Mod+O"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Save labels (JSON)", "SAVELABELS", "Mod+S", requiresCloud: true),
            CommandMenuEntry.Action("Save labels to LAS", "SAVELABELS LAS", requiresCloud: true),
            CommandMenuEntry.Action("Load labels", "LOADLABELS", requiresCloud: true),
            CommandMenuEntry.Action("Clear labels", "CLEARLABELS", requiresCloud: true)),

        CommandMenuEntry.Submenu("Edit",
            CommandMenuEntry.Action("Undo", "UNDO", "Mod+Z"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Confirm", "CONFIRM", "Mod+Return"),
            CommandMenuEntry.Action("Cancel", "CANCEL", "Escape"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Fit to points", "FIT"),
            CommandMenuEntry.Action("Fit to ground", "FITGROUND")),

        CommandMenuEntry.Submenu("Select",
            CommandMenuEntry.Action("Navigate mode", "NAVIGATE", "", CheckStates.ModeNavigate),
            CommandMenuEntry.Action("Label mode", "LABELMODE", "", CheckStates.ModeLabel),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Box", "SELECT B", "", CheckStates.ToolBox),
            CommandMenuEntry.Action("Sphere", "SELECT S", "", CheckStates.ToolSphere),
            CommandMenuEntry.Action("Cylinder", "SELECT C", "", CheckStates.ToolCylinder)),

        CommandMenuEntry.Submenu("View",
            CommandMenuEntry.Action("Zoom extents", "ZOOM E", "Mod+0"),
            CommandMenuEntry.Action("Zoom to object", "ZOOM C"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Submenu("Standard views",
                CommandMenuEntry.Action("Top", "VIEW TOP", "Mod+1"),
                CommandMenuEntry.Action("Bottom", "VIEW BOTTOM"),
                CommandMenuEntry.Action("Front", "VIEW FRONT", "Mod+2"),
                CommandMenuEntry.Action("Back", "VIEW BACK"),
                CommandMenuEntry.Action("Left", "VIEW LEFT", "Mod+3"),
                CommandMenuEntry.Action("Right", "VIEW RIGHT"),
                CommandMenuEntry.Action("Isometric", "VIEW ISOMETRIC", "Mod+4")),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Perspective", "PROJECTION PERSPECTIVE", "", CheckStates.Perspective),
            CommandMenuEntry.Action("Parallel", "PROJECTION PARALLEL", "", CheckStates.Parallel),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Submenu("Viewports",
                CommandMenuEntry.Action("Single", "VPORTS SI TOP"),
                CommandMenuEntry.Action("Two vertical", "VPORTS 2 V TOP"),
                CommandMenuEntry.Action("Two horizontal", "VPORTS 2 H TOP")),
            CommandMenuEntry.Submenu("Color by",
                CommandMenuEntry.Action("RGB", "COLORBY RGB", requiresCloud: true),
                CommandMenuEntry.Action("Height", "COLORBY HEIGHT", requiresCloud: true),
                CommandMenuEntry.Action("Classification", "COLORBY CLASS", requiresCloud: true),
                CommandMenuEntry.Action("Intensity", "COLORBY INTENSITY", requiresCloud: true),
                CommandMenuEntry.Action("Return", "COLORBY RETURN", requiresCloud: true),
                CommandMenuEntry.Separator,
                CommandMenuEntry.Action("Reset", "COLORBY CLEAR", requiresCloud: true)),
            CommandMenuEntry.Submenu("Point size",
                CommandMenuEntry.Action("1 px", "POINTSIZE 1"),
                CommandMenuEntry.Action("2 px", "POINTSIZE 2"),
                CommandMenuEntry.Action("3 px", "POINTSIZE 3"),
                CommandMenuEntry.Action("5 px", "POINTSIZE 5")),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Attributes", "ATTRIBUTES", requiresCloud: true),
            CommandMenuEntry.Action("Filter...", "FILTER", requiresCloud: true)),

        CommandMenuEntry.Submenu("Window",
            CommandMenuEntry.Action("Label registry", "LABELS", "Mod+L"),
            CommandMenuEntry.Action("Command history", "HISTORY", "F2"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Reset viewer", "RESET")),

        CommandMenuEntry.Submenu("Help",
            CommandMenuEntry.Action("Command list", "HELP", "F1"))
    ];

    /// <summary>Snapshot-state names referenced by <see cref="CommandMenuEntry.CheckState"/>.</summary>
    public static class CheckStates
    {
        public const string ModeNavigate = "mode.navigate";
        public const string ModeLabel = "mode.label";
        public const string ToolBox = "tool.box";
        public const string ToolSphere = "tool.sphere";
        public const string ToolCylinder = "tool.cylinder";
        public const string Perspective = "projection.perspective";
        public const string Parallel = "projection.parallel";
    }

    /// <summary>Resolves an entry's check mark from the current viewer state.</summary>
    public static bool IsChecked(string checkState, ViewerStatusSnapshot status) => checkState switch
    {
        CheckStates.ModeNavigate => status.Mode == Selection.InteractionMode.Navigate,
        CheckStates.ModeLabel => status.Mode == Selection.InteractionMode.Label,
        CheckStates.ToolBox => status.ActiveTool == Selection.SelectionToolType.Box,
        CheckStates.ToolSphere => status.ActiveTool == Selection.SelectionToolType.Sphere,
        CheckStates.ToolCylinder => status.ActiveTool == Selection.SelectionToolType.Cylinder,
        CheckStates.Perspective => status.IsPerspective,
        CheckStates.Parallel => !status.IsPerspective,
        _ => false
    };

    /// <summary>Renders a portable shortcut for display, e.g. "Mod+O" → "⌘O" or "Ctrl+O".</summary>
    public static string DisplayShortcut(string shortcut, bool macOs) =>
        shortcut.Length == 0
            ? ""
            : macOs
                ? shortcut.Replace("Mod+", "⌘").Replace("Shift+", "⇧").Replace("Return", "⏎").Replace("Escape", "⎋")
                : shortcut.Replace("Mod+", "Ctrl+");
}
