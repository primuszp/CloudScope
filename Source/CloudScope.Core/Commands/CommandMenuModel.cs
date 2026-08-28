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
            CommandMenuEntry.Action("Open tile store...", "OPENSTORE"),
            CommandMenuEntry.Action("Add tile store as layer...", "ADDSTORE"),
            CommandMenuEntry.Action("Index a LAS file...", "INDEX"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Layers", "LAYER List"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Save labels (JSON)", "SAVELABELS Json Default", "Mod+S", requiresCloud: true),
            CommandMenuEntry.Action("Save labels to LAS", "SAVELABELS Las", requiresCloud: true),
            CommandMenuEntry.Action("Load labels", "LOADLABELS Default", requiresCloud: true),
            CommandMenuEntry.Action("Clear labels", "CLEARLABELS", requiresCloud: true),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Run script...", "SCRIPT"),
            CommandMenuEntry.Action("Quit", "QUIT", "Mod+Q")),

        CommandMenuEntry.Submenu("Edit",
            CommandMenuEntry.Action("Undo", "UNDO 1", "Mod+Z"),
            CommandMenuEntry.Action("Redo", "REDO 1", "Mod+Shift+Z"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Confirm", "CONFIRM", "Mod+Return"),
            CommandMenuEntry.Action("Cancel", "CANCEL", "Escape"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("3D polyline...", "3DPOLY"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Move selection...", "MOVE", requiresCloud: true),
            CommandMenuEntry.Action("Rotate selection...", "ROTATE", requiresCloud: true),
            CommandMenuEntry.Action("Scale selection...", "SCALE", requiresCloud: true),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Fit to points", "FIT Points"),
            CommandMenuEntry.Action("Fit to ground", "FIT Ground")),

        CommandMenuEntry.Submenu("Select",
            CommandMenuEntry.Action("Navigate mode", "NAVIGATE", "", CheckStates.ModeNavigate),
            CommandMenuEntry.Action("Label mode", "LABELMODE", "", CheckStates.ModeLabel),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Box", "SELECT Box", "", CheckStates.ToolBox),
            CommandMenuEntry.Action("Sphere", "SELECT Sphere", "", CheckStates.ToolSphere),
            CommandMenuEntry.Action("Cylinder", "SELECT Cylinder", "", CheckStates.ToolCylinder),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Erase labels in selection", "UNLABEL", requiresCloud: true)),

        CommandMenuEntry.Submenu("View",
            CommandMenuEntry.Action("Zoom extents", "ZOOM Extents", "Mod+0"),
            CommandMenuEntry.Action("Zoom to object", "ZOOM Center"),
            CommandMenuEntry.Action("Zoom window", "ZOOM Window"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Pan...", "PAN"),
            CommandMenuEntry.Action("Orbit...", "ORBIT"),
            CommandMenuEntry.Action("Set pivot...", "PIVOT"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Submenu("Standard views",
                CommandMenuEntry.Action("Top", "VIEW Top", "Mod+1"),
                CommandMenuEntry.Action("Bottom", "VIEW Bottom"),
                CommandMenuEntry.Action("Front", "VIEW Front", "Mod+2"),
                CommandMenuEntry.Action("Back", "VIEW BAck"),
                CommandMenuEntry.Action("Left", "VIEW Left", "Mod+3"),
                CommandMenuEntry.Action("Right", "VIEW Right"),
                CommandMenuEntry.Action("Isometric", "VIEW Isometric", "Mod+4")),
            CommandMenuEntry.Submenu("Named views",
                CommandMenuEntry.Action("Save current view...", "VIEW Save"),
                CommandMenuEntry.Action("Restore view...", "VIEW Restore"),
                CommandMenuEntry.Action("List views", "VIEW LIst"),
                CommandMenuEntry.Action("Delete view...", "VIEW DElete")),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Perspective", "PROJECTION Perspective", "", CheckStates.Perspective),
            CommandMenuEntry.Action("Parallel", "PROJECTION PArallel", "", CheckStates.Parallel),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Submenu("Viewports",
                CommandMenuEntry.Action("Single", "VPORTS Single Top"),
                CommandMenuEntry.Action("Two vertical", "VPORTS Two Vertical Top"),
                CommandMenuEntry.Action("Two horizontal", "VPORTS Two Horizontal Top"),
                CommandMenuEntry.Action("Four tiled", "VPORTS 4 Top"),
                CommandMenuEntry.Action("Nine tiled", "VPORTS 9 Top"),
                CommandMenuEntry.Action("Previous", "VPORTS PRevious Top")),
            CommandMenuEntry.Submenu("Cross-section",
                CommandMenuEntry.Action("New...", "XSECTION", requiresCloud: true),
                CommandMenuEntry.Action("Show in active viewport", "XSECTION View", requiresCloud: true),
                CommandMenuEntry.Action("Flip direction", "XSECTION Flip", requiresCloud: true),
                CommandMenuEntry.Action("Clear", "XSECTION CLear", requiresCloud: true)),
            CommandMenuEntry.Submenu("Color by",
                CommandMenuEntry.Action("RGB", "COLORBY Rgb", requiresCloud: true),
                CommandMenuEntry.Action("Height", "COLORBY Height", requiresCloud: true),
                CommandMenuEntry.Action("Classification", "COLORBY Class", requiresCloud: true),
                CommandMenuEntry.Action("Intensity", "COLORBY Intensity", requiresCloud: true),
                CommandMenuEntry.Action("Return", "COLORBY ReTurn", requiresCloud: true),
                CommandMenuEntry.Separator,
                CommandMenuEntry.Action("Reset", "COLORBY CLear", requiresCloud: true)),
            CommandMenuEntry.Submenu("Point size",
                CommandMenuEntry.Action("1 px", "POINTSIZE 1"),
                CommandMenuEntry.Action("2 px", "POINTSIZE 2"),
                CommandMenuEntry.Action("3 px", "POINTSIZE 3"),
                CommandMenuEntry.Action("5 px", "POINTSIZE 5")),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("Filter...", "FILTER", requiresCloud: true)),

        CommandMenuEntry.Submenu("Inquire",
            CommandMenuEntry.Action("Status", "STATUS"),
            CommandMenuEntry.Action("Attributes", "ATTRIBUTES All", requiresCloud: true),
            CommandMenuEntry.Action("Label statistics", "LABELSTAT", requiresCloud: true),
            CommandMenuEntry.Action("Store info", "STOREINFO"),
            CommandMenuEntry.Action("Frame timing", "TIME"),
            CommandMenuEntry.Separator,
            CommandMenuEntry.Action("System variables", "SETVAR ? *"),
            CommandMenuEntry.Action("Graphics configuration", "GRAPHICSCONFIG"),
            CommandMenuEntry.Action("Point cloud budget", "POINTCLOUDCONFIG Show")),

        CommandMenuEntry.Submenu("Window",
            CommandMenuEntry.Action("Label registry", "LABELS", "Mod+L"),
            CommandMenuEntry.Action("Command history", "HISTORY", "F2"),
            CommandMenuEntry.Action("Command line", "COMMANDLINE Toggle", "Mod+9", CheckStates.CommandLine),
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
        public const string CommandLine = "window.commandline";
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
        CheckStates.CommandLine => status.CommandLineVisible,
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
