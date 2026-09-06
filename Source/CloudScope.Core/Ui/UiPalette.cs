namespace CloudScope.Ui;

/// <summary>
/// CloudScope's design tokens — colours, metrics and font stacks — defined once for every
/// shell. The Avalonia resource dictionary and the ImGui style are both built from these
/// values, so the two user interfaces cannot drift into different-looking versions of the
/// same product.
///
/// The system is dark-only and reads as AutoCAD's graphite dark theme: near-black canvas,
/// graphite-grey surfaces, hairline borders, one azure accent. A point-cloud viewport is
/// judged against its surroundings, so the shell stays dark on every platform.
/// </summary>
public static class UiPalette
{
    // ── Colour tokens (0xRRGGBB) ────────────────────────────────────────────

    /// <summary>The 3D canvas behind every viewport — the deepest black in the system.</summary>
    public const uint ViewportBackdrop = 0x0E0F11;

    /// <summary>Sunken wells: the command window body, popups, inset lists.</summary>
    public const uint SurfaceDeep = 0x161719;

    /// <summary>Default panel body: the inspector, dialog surfaces.</summary>
    public const uint Surface = 0x1E2022;

    /// <summary>Raised surfaces: menu bar, status bar, cards.</summary>
    public const uint SurfaceAlt = 0x26282B;

    /// <summary>The unified titlebar / tool strip band.</summary>
    public const uint Graphite = 0x303234;

    /// <summary>Pointer-over fill for buttons, rows and menu items.</summary>
    public const uint SurfaceHover = 0x34373B;

    /// <summary>Hairline dividers and control outlines at rest.</summary>
    public const uint Border = 0x3A3D40;

    /// <summary>Outline of a focused or actively raised control.</summary>
    public const uint BorderStrong = 0x4A4E52;

    /// <summary>Primary text.</summary>
    public const uint Text = 0xD8DADE;

    /// <summary>Secondary text: labels, captions, section headers.</summary>
    public const uint TextDim = 0x8A8F96;

    /// <summary>Disabled text and faint separators.</summary>
    public const uint TextFaint = 0x5C6167;

    /// <summary>The one accent: prompt text, active tool, focus, selection edge.</summary>
    public const uint Accent = 0x3E8FD0;

    /// <summary>Accent under the pointer.</summary>
    public const uint AccentBright = 0x5BA6E4;

    /// <summary>Pressed accent and dim accent fills (e.g. the status strip tint).</summary>
    public const uint AccentDim = 0x2C6B9E;

    /// <summary>Selected list row / highlighted completion candidate fill.</summary>
    public const uint SelectionFill = 0x24384A;

    public const uint Error = 0xE06C6C;
    public const uint Ok = 0x5FB57A;
    public const uint Warn = 0xE0A24E;

    /// <summary>Echoed command lines are as quiet as secondary text.</summary>
    public const uint EntryEcho = 0x8A8F96;

    /// <summary>Plain command output sits at primary-text weight.</summary>
    public const uint EntryOutput = 0xD8DADE;

    // ── Metric tokens ──────────────────────────────────────────────────────

    /// <summary>Corner radius for buttons, inputs and small controls.</summary>
    public const double RadiusControl = 5;

    /// <summary>Corner radius for cards, popups and grouped containers.</summary>
    public const double RadiusCard = 6;

    /// <summary>Thickness of a hairline divider or control outline.</summary>
    public const double HairlineThickness = 1;

    public const double FontSizeBody = 12;
    public const double FontSizeSmall = 10;
    public const double FontSizeMono = 12;

    /// <summary>The 4 / 8 / 12 / 16 spacing steps every layout is built from.</summary>
    public const double Space1 = 4;
    public const double Space2 = 8;
    public const double Space3 = 12;
    public const double Space4 = 16;

    // ── Font stacks ────────────────────────────────────────────────────────

    /// <summary>First family that exists on the platform wins.</summary>
    public const string UiFontStack = "SF Pro Text, Helvetica Neue, Segoe UI Variable Text, Segoe UI, Inter, sans-serif";

    public const string MonoFontStack = "SF Mono, Menlo, Cascadia Mono, Consolas, DejaVu Sans Mono, monospace";

    // ── Derivations ───────────────────────────────────────────────────────

    /// <summary>Colour of a command-line history line of the given kind.</summary>
    public static uint EntryColor(Commands.CommandEntryKind kind) => kind switch
    {
        Commands.CommandEntryKind.Echo => EntryEcho,
        Commands.CommandEntryKind.Prompt => Accent,
        Commands.CommandEntryKind.Error => Error,
        Commands.CommandEntryKind.Banner => TextDim,
        _ => EntryOutput
    };

    public static byte R(uint color) => (byte)(color >> 16);
    public static byte G(uint color) => (byte)(color >> 8);
    public static byte B(uint color) => (byte)color;

    /// <summary>Named brushes the Avalonia shell exposes as dynamic resources.</summary>
    public static IReadOnlyList<(string Key, uint Color)> NamedColors =>
    [
        ("CsAccent", Accent),
        ("CsAccentBright", AccentBright),
        ("CsAccentDim", AccentDim),
        ("CsSelectionFill", SelectionFill),
        ("CsSurface", Surface),
        ("CsSurfaceAlt", SurfaceAlt),
        ("CsSurfaceDeep", SurfaceDeep),
        ("CsSurfaceHover", SurfaceHover),
        ("CsGraphite", Graphite),
        ("CsViewportBackdrop", ViewportBackdrop),
        ("CsBorder", Border),
        ("CsBorderStrong", BorderStrong),
        ("CsText", Text),
        ("CsTextDim", TextDim),
        ("CsTextFaint", TextFaint),
        ("CsError", Error),
        ("CsOk", Ok),
        ("CsWarn", Warn)
    ];

    /// <summary>Named scalar metrics the Avalonia shell exposes as dynamic resources.</summary>
    public static IReadOnlyList<(string Key, double Value)> NamedMetrics =>
    [
        ("CsRadiusControl", RadiusControl),
        ("CsRadiusCard", RadiusCard),
        ("CsHairline", HairlineThickness),
        ("CsFontSizeBody", FontSizeBody),
        ("CsFontSizeSmall", FontSizeSmall),
        ("CsSpace1", Space1),
        ("CsSpace2", Space2),
        ("CsSpace3", Space3),
        ("CsSpace4", Space4)
    ];
}
