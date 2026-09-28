namespace CloudScope.Commands;

/// <summary>How much room a ribbon button takes: a tall icon-over-caption button, or a row
/// in a three-high stack of small icon-beside-caption buttons.</summary>
public enum RibbonButtonSize
{
    Large,
    Small
}

/// <summary>
/// One ribbon button. Like a menu entry it is only a command string — the ribbon never calls
/// the viewer directly — plus the icon key a shell draws and an optional check state that
/// lights the button while its mode is active.
/// </summary>
public sealed record RibbonButton(
    string Caption,
    string Command,
    string Icon,
    RibbonButtonSize Size = RibbonButtonSize.Small,
    string CheckState = "",
    string Tooltip = "",
    bool RequiresCloud = false);

/// <summary>A titled group of buttons, AutoCAD's "panel" (title drawn under the buttons).</summary>
public sealed record RibbonPanel(string Title, IReadOnlyList<RibbonButton> Buttons);

/// <summary>A ribbon tab: a row of panels.</summary>
public sealed record RibbonTab(string Title, IReadOnlyList<RibbonPanel> Panels);

/// <summary>
/// The single definition of CloudScope's ribbon. It is a projection of the command set the
/// same way <see cref="CommandMenu"/> is: shells draw it, the command checks verify every
/// button resolves to a registered command, and nothing the ribbon does is out of reach of
/// the command line.
/// </summary>
public static class CommandRibbon
{
    private static RibbonButton Large(string caption, string command, string icon, string checkState = "",
        string tooltip = "", bool requiresCloud = false) =>
        new(caption, command, icon, RibbonButtonSize.Large, checkState, tooltip, requiresCloud);

    private static RibbonButton Small(string caption, string command, string icon, string checkState = "",
        string tooltip = "", bool requiresCloud = false) =>
        new(caption, command, icon, RibbonButtonSize.Small, checkState, tooltip, requiresCloud);

    private static RibbonPanel Panel(string title, params RibbonButton[] buttons) => new(title, buttons);

    public static IReadOnlyList<RibbonTab> Build() =>
    [
        new RibbonTab("Home",
        [
            Panel("Cloud",
                Large("Open", "OPEN", RibbonIcons.Open, tooltip: "Open a LAS/LAZ point cloud"),
                Small("PLY", "OPENPLY", RibbonIcons.Import, tooltip: "Import PLY vertices"),
                Small("E57", "OPENE57", RibbonIcons.Scanner, tooltip: "Import E57 scans"),
                Small("Tile store", "ADDSTORE", RibbonIcons.Layers, tooltip: "Add a point tile store as a layer")),
            Panel("Mode",
                Large("Navigate", "NAVIGATE", RibbonIcons.Navigate, CommandMenu.CheckStates.ModeNavigate),
                Large("Label", "LABELMODE", RibbonIcons.Label, CommandMenu.CheckStates.ModeLabel)),
            Panel("Selection",
                Small("Box", "SELECT Box", RibbonIcons.Box, CommandMenu.CheckStates.ToolBox),
                Small("Sphere", "SELECT Sphere", RibbonIcons.Sphere, CommandMenu.CheckStates.ToolSphere),
                Small("Cylinder", "SELECT Cylinder", RibbonIcons.Cylinder, CommandMenu.CheckStates.ToolCylinder),
                Small("Confirm", "CONFIRM", RibbonIcons.Confirm),
                Small("Cancel", "CANCEL", RibbonIcons.Cancel),
                Small("Unlabel", "UNLABEL", RibbonIcons.Erase, requiresCloud: true)),
            Panel("Modify",
                Small("Move", "MOVE", RibbonIcons.Move, requiresCloud: true),
                Small("Rotate", "ROTATE", RibbonIcons.Rotate, requiresCloud: true),
                Small("Scale", "SCALE", RibbonIcons.Scale, requiresCloud: true),
                Small("Fit", "FIT Points", RibbonIcons.Fit),
                Small("Fit ground", "FIT Ground", RibbonIcons.Ground),
                Small("Undo", "UNDO 1", RibbonIcons.Undo)),
            Panel("Navigate",
                Large("Extents", "ZOOM Extents", RibbonIcons.ZoomExtents),
                Small("Window", "ZOOM Window", RibbonIcons.ZoomWindow),
                Small("Pan", "PAN", RibbonIcons.Pan),
                Small("Orbit", "ORBIT", RibbonIcons.Orbit),
                Small("Pivot", "PIVOT", RibbonIcons.Pivot),
                Small("Previous", "ZOOM PRevious", RibbonIcons.Previous),
                Small("Redo", "REDO 1", RibbonIcons.Redo))
        ]),

        new RibbonTab("View",
        [
            Panel("Views",
                Large("Iso", "VIEW Isometric", RibbonIcons.ViewIso),
                Small("Top", "VIEW Top", RibbonIcons.ViewTop),
                Small("Front", "VIEW Front", RibbonIcons.ViewFront),
                Small("Left", "VIEW Left", RibbonIcons.ViewSide),
                Small("Right", "VIEW Right", RibbonIcons.ViewSide),
                Small("Back", "VIEW BAck", RibbonIcons.ViewFront),
                Small("Bottom", "VIEW Bottom", RibbonIcons.ViewTop)),
            Panel("Named views",
                Small("Save view", "VIEW Save", RibbonIcons.Save),
                Small("Restore", "VIEW Restore", RibbonIcons.Previous),
                Small("List", "VIEW LIst", RibbonIcons.List)),
            Panel("Projection",
                Large("Perspective", "PROJECTION Perspective", RibbonIcons.Perspective, CommandMenu.CheckStates.Perspective),
                Large("Parallel", "PROJECTION PArallel", RibbonIcons.Parallel, CommandMenu.CheckStates.Parallel)),
            Panel("Viewports",
                Large("Single", "VPORTS Single Top", RibbonIcons.ViewportSingle),
                Small("Two vertical", "VPORTS Two Vertical Top", RibbonIcons.ViewportTwo),
                Small("Two horizontal", "VPORTS Two Horizontal Top", RibbonIcons.ViewportTwoH),
                Small("Four", "VPORTS 4 Top", RibbonIcons.ViewportFour),
                Small("Nine", "VPORTS 9 Top", RibbonIcons.ViewportNine),
                Small("Previous", "VPORTS PRevious Top", RibbonIcons.Previous)),
            Panel("Palettes",
                Small("Explorer", "EXPLORER Toggle", RibbonIcons.Explorer, CommandMenu.CheckStates.Explorer),
                Small("Properties", "PROPERTIES Toggle", RibbonIcons.Properties, CommandMenu.CheckStates.Properties),
                Small("Command line", "COMMANDLINE Toggle", RibbonIcons.CommandLine, CommandMenu.CheckStates.CommandLine),
                Small("History", "HISTORY", RibbonIcons.History),
                Small("Dock left", "EXPLORER Left", RibbonIcons.DockLeft, CommandMenu.CheckStates.PalettesLeft),
                Small("Dock right", "EXPLORER Right", RibbonIcons.DockRight, CommandMenu.CheckStates.PalettesRight))
        ]),

        new RibbonTab("Display",
        [
            Panel("Color by",
                Large("RGB", "COLORBY Rgb", RibbonIcons.ColorRgb, requiresCloud: true),
                Small("Height", "COLORBY Height", RibbonIcons.ColorHeight, requiresCloud: true),
                Small("Class", "COLORBY Class", RibbonIcons.ColorClass, requiresCloud: true),
                Small("Intensity", "COLORBY Intensity", RibbonIcons.ColorIntensity, requiresCloud: true),
                Small("Return", "COLORBY ReTurn", RibbonIcons.ColorReturn, requiresCloud: true),
                Small("Reset", "COLORBY CLear", RibbonIcons.Cancel, requiresCloud: true)),
            Panel("Points",
                Small("Larger", "POINTSIZE +", RibbonIcons.PointLarger),
                Small("Smaller", "POINTSIZE -", RibbonIcons.PointSmaller),
                Small("1 px", "POINTSIZE 1", RibbonIcons.Point),
                Small("2 px", "POINTSIZE 2", RibbonIcons.Point),
                Small("3 px", "POINTSIZE 3", RibbonIcons.Point),
                Small("5 px", "POINTSIZE 5", RibbonIcons.Point)),
            Panel("Density",
                Large("Filter", "FILTER", RibbonIcons.Filter, requiresCloud: true),
                Small("Clear filter", "FILTER CLear", RibbonIcons.Cancel, requiresCloud: true),
                Small("Thin", "THIN", RibbonIcons.Thin, requiresCloud: true),
                Small("Full density", "THIN 100", RibbonIcons.PointLarger, requiresCloud: true)),
            Panel("Budget",
                Small("Point budget", "POINTCLOUDCONFIG Show", RibbonIcons.Gauge),
                Small("Graphics", "GRAPHICSCONFIG", RibbonIcons.Settings),
                Small("Reset viewer", "RESET", RibbonIcons.Reset, tooltip: "Close everything and reset the viewer"))
        ]),

        new RibbonTab("Label",
        [
            Panel("Labels",
                Large("Registry", "LABELS", RibbonIcons.Registry),
                Small("Active label", "LABEL", RibbonIcons.Label),
                Small("Instance", "INSTANCE", RibbonIcons.Instance),
                Small("Clear instance", "INSTANCE CLear", RibbonIcons.Cancel),
                Small("Define", "LABELDEF", RibbonIcons.Add),
                Small("Definitions", "LABELDEF List", RibbonIcons.List),
                Small("Delete definition", "LABELDEF DElete", RibbonIcons.Erase)),
            Panel("Segmentation",
                Large("Ground", "GROUNDSEG", RibbonIcons.Ground, tooltip: "Classify terrain (CSF)", requiresCloud: true),
                Large("Tree", "TREESEG", RibbonIcons.Tree, tooltip: "Segment one tree from a trunk seed", requiresCloud: true)),
            Panel("Label data",
                Small("Save JSON", "SAVELABELS Json Default", RibbonIcons.Save, requiresCloud: true),
                Small("Save to LAS", "SAVELABELS Las", RibbonIcons.Export, requiresCloud: true),
                Small("Load", "LOADLABELS Default", RibbonIcons.Open, requiresCloud: true),
                Small("Statistics", "LABELSTAT", RibbonIcons.Chart, requiresCloud: true),
                Small("Clear all", "CLEARLABELS", RibbonIcons.Erase, requiresCloud: true))
        ]),

        new RibbonTab("Analyze",
        [
            Panel("Section",
                Large("Section", "XSECTION", RibbonIcons.Section, requiresCloud: true),
                Small("Show", "XSECTION View", RibbonIcons.ViewFront, requiresCloud: true),
                Small("Flip", "XSECTION Flip", RibbonIcons.Flip, requiresCloud: true),
                Small("Width", "XSECTION Width", RibbonIcons.Scale, requiresCloud: true),
                Small("List", "XSECTION List", RibbonIcons.List, requiresCloud: true),
                Small("Clear", "XSECTION CLear", RibbonIcons.Cancel, requiresCloud: true)),
            Panel("Surface",
                Large("Reconstruct", "RECONSTRUCT", RibbonIcons.Mesh, requiresCloud: true),
                Small("Show", "SURFACE ON", RibbonIcons.Visible, CommandMenu.CheckStates.Surface),
                Small("Hide", "SURFACE OFf", RibbonIcons.Hidden),
                Small("Clear", "SURFACE CLear", RibbonIcons.Cancel)),
            Panel("Draw",
                Large("Polyline", "PLINE", RibbonIcons.Polyline),
                Small("3D polyline", "3DPOLY", RibbonIcons.Polyline3D),
                Small("Edit polyline", "PEDIT", RibbonIcons.Edit),
                Small("Ortho", "ORTHO Toggle", RibbonIcons.Ortho, CommandMenu.CheckStates.Ortho)),
            Panel("Inquiry",
                Small("Status", "STATUS", RibbonIcons.Info),
                Small("Attributes", "ATTRIBUTES All", RibbonIcons.Chart, requiresCloud: true),
                Small("Store info", "STOREINFO", RibbonIcons.Layers),
                Small("Frame timing", "TIME", RibbonIcons.Gauge),
                Small("Variables", "SETVAR ? *", RibbonIcons.Settings),
                Small("Get variable", "GETVAR", RibbonIcons.Settings))
        ]),

        new RibbonTab("Output",
        [
            Panel("Export points",
                Large("PLY", "EXPORT PlyBinary", RibbonIcons.Export, tooltip: "Binary PLY", requiresCloud: true),
                Small("PLY ASCII", "EXPORT PlyAscii", RibbonIcons.Export, requiresCloud: true),
                Small("XYZ", "EXPORT Xyz", RibbonIcons.Export, requiresCloud: true),
                Small("PTS", "EXPORT Pts", RibbonIcons.Export, requiresCloud: true),
                Small("CSV", "EXPORT Csv", RibbonIcons.Export, requiresCloud: true)),
            Panel("Export geometry",
                Large("Surface OBJ", "EXPORTOBJ", RibbonIcons.Mesh, requiresCloud: true),
                Small("Save polylines", "SAVEPOLYLINES", RibbonIcons.Save),
                Small("Load polylines", "LOADPOLYLINES", RibbonIcons.Open)),
            Panel("Tile stores",
                Large("Index LAS", "INDEX", RibbonIcons.Index),
                Small("Open store", "OPENSTORE", RibbonIcons.Open),
                Small("Add store", "ADDSTORE", RibbonIcons.Layers),
                Small("Layers", "LAYER List", RibbonIcons.List)),
            Panel("Automation",
                Large("Script", "SCRIPT", RibbonIcons.Script),
                Small("API on", "API ON", RibbonIcons.Api),
                Small("API off", "API OFf", RibbonIcons.Cancel),
                Small("Commands", "COMMANDS", RibbonIcons.List),
                Small("Coverage", "COVERAGE", RibbonIcons.Chart),
                Small("Help", "HELP", RibbonIcons.Info))
        ])
    ];
}

/// <summary>
/// Icon keys the ribbon names. A shell maps each key to its own artwork; keeping the keys
/// here lets the model stay free of any drawing technology.
/// </summary>
public static class RibbonIcons
{
    public const string Open = "open";
    public const string Import = "import";
    public const string Scanner = "scanner";
    public const string Layers = "layers";
    public const string Navigate = "navigate";
    public const string Label = "label";
    public const string Box = "box";
    public const string Sphere = "sphere";
    public const string Cylinder = "cylinder";
    public const string Confirm = "confirm";
    public const string Cancel = "cancel";
    public const string Erase = "erase";
    public const string Move = "move";
    public const string Rotate = "rotate";
    public const string Scale = "scale";
    public const string Fit = "fit";
    public const string Ground = "ground";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string ZoomExtents = "zoom-extents";
    public const string ZoomWindow = "zoom-window";
    public const string Pan = "pan";
    public const string Orbit = "orbit";
    public const string Pivot = "pivot";
    public const string Previous = "previous";
    public const string ViewIso = "view-iso";
    public const string ViewTop = "view-top";
    public const string ViewFront = "view-front";
    public const string ViewSide = "view-side";
    public const string Save = "save";
    public const string List = "list";
    public const string Perspective = "perspective";
    public const string Parallel = "parallel";
    public const string ViewportSingle = "vp-single";
    public const string ViewportTwo = "vp-two";
    public const string ViewportTwoH = "vp-two-h";
    public const string ViewportFour = "vp-four";
    public const string ViewportNine = "vp-nine";
    public const string Explorer = "explorer";
    public const string Properties = "properties";
    public const string CommandLine = "command-line";
    public const string History = "history";
    public const string DockLeft = "dock-left";
    public const string DockRight = "dock-right";
    public const string ColorRgb = "color-rgb";
    public const string ColorHeight = "color-height";
    public const string ColorClass = "color-class";
    public const string ColorIntensity = "color-intensity";
    public const string ColorReturn = "color-return";
    public const string PointLarger = "point-larger";
    public const string PointSmaller = "point-smaller";
    public const string Point = "point";
    public const string Filter = "filter";
    public const string Thin = "thin";
    public const string Gauge = "gauge";
    public const string Settings = "settings";
    public const string Reset = "reset";
    public const string Registry = "registry";
    public const string Instance = "instance";
    public const string Add = "add";
    public const string Tree = "tree";
    public const string Export = "export";
    public const string Chart = "chart";
    public const string Section = "section";
    public const string Flip = "flip";
    public const string Mesh = "mesh";
    public const string Visible = "visible";
    public const string Hidden = "hidden";
    public const string Polyline = "polyline";
    public const string Polyline3D = "polyline-3d";
    public const string Edit = "edit";
    public const string Ortho = "ortho";
    public const string Info = "info";
    public const string Index = "index";
    public const string Script = "script";
    public const string Api = "api";
    public const string Cloud = "cloud";
}
