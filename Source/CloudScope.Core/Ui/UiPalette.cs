namespace CloudScope.Ui;

/// <summary>
/// CloudScope's design tokens — colours, metrics and font stacks — defined once for every
/// shell. The Avalonia resource dictionary and the ImGui style are both built from these
/// values, so the two user interfaces cannot drift into different-looking versions of the
/// same product.
///
/// The system is dark-only and reads as AutoCAD's graphite dark theme: near-black canvas,
/// neutral graphite-grey surfaces, hairline borders, and no accent hue — AutoCAD's dark
/// chrome carries none, and the Autodesk brand's UI neutrals are the charcoal ramp. Emphasis
/// (prompt text, focus, the active tool) is carried by a bright neutral grey; only the
/// Ok / Warn / Error signals are coloured. A point-cloud viewport is judged against its
/// surroundings, so the shell stays dark on every platform.
/// </summary>
public static class UiPalette
{
    // ── Colour tokens (0xRRGGBB) — neutral greyscale, no hue ────────────────

    /// <summary>The 3D canvas behind every viewport — the deepest black in the system.</summary>
    public const uint ViewportBackdrop = 0x0F0F0F;

    /// <summary>Sunken wells: the command window body, popups, inset lists.</summary>
    public const uint SurfaceDeep = 0x171717;

    /// <summary>Default panel body: the inspector, dialog surfaces.</summary>
    public const uint Surface = 0x1F1F1F;

    /// <summary>Raised surfaces: menu bar, status bar, cards.</summary>
    public const uint SurfaceAlt = 0x272727;

    /// <summary>The unified titlebar / tool strip band (≈ Autodesk charcoal-900).</summary>
    public const uint Graphite = 0x323232;

    /// <summary>Pointer-over fill for buttons, rows and menu items.</summary>
    public const uint SurfaceHover = 0x373737;

    /// <summary>Hairline dividers and control outlines at rest.</summary>
    public const uint Border = 0x3D3D3D;

    /// <summary>Outline of a focused or actively raised control.</summary>
    public const uint BorderStrong = 0x4E4E4E;

    /// <summary>Primary text.</summary>
    public const uint Text = 0xDADADA;

    /// <summary>Secondary text: labels, captions, section headers (≈ Autodesk charcoal-700).</summary>
    public const uint TextDim = 0x909090;

    /// <summary>Disabled text and faint separators.</summary>
    public const uint TextFaint = 0x616161;

    /// <summary>Emphasis, not a hue: prompt text, active tool glyph, focus outline.</summary>
    public const uint Accent = 0xC2C2C2;

    /// <summary>Emphasis under the pointer.</summary>
    public const uint AccentBright = 0xDEDEDE;

    /// <summary>Lifted-graphite fill for a pressed or active control and the status strip.</summary>
    public const uint AccentDim = 0x3E3E3E;

    /// <summary>Selected list row / highlighted completion candidate fill.</summary>
    public const uint SelectionFill = 0x343434;

    /// <summary>
    /// The frame around the 3D viewport. Its own token so the visible edge of the viewport
    /// can be tuned — colour here, thickness in <see cref="ViewportBorderThickness"/> — without
    /// touching the shared hairline. The embedded GL child window is created borderless, so
    /// this is the only line around the viewport.
    /// </summary>
    public const uint ViewportBorder = Border;

    public const uint Error = 0xE06C6C;
    public const uint Ok = 0x5FB57A;
    public const uint Warn = 0xE0A24E;

    /// <summary>Echoed command lines are as quiet as secondary text.</summary>
    public const uint EntryEcho = 0x909090;

    /// <summary>Plain command output sits at primary-text weight.</summary>
    public const uint EntryOutput = 0xDADADA;

    // ── Metric tokens ──────────────────────────────────────────────────────

    /// <summary>Corner radius for buttons, inputs and small controls — tight and technical.</summary>
    public const double RadiusControl = 3;

    /// <summary>Corner radius for cards, popups and grouped containers.</summary>
    public const double RadiusCard = 5;

    /// <summary>Thickness of every divider and control outline in the shell — one hairline.</summary>
    public const double HairlineThickness = 1;

    /// <summary>Thickness of the <see cref="ViewportBorder"/> frame, in device-independent
    /// pixels. Set to 0 to let the panel seams alone bound the viewport.</summary>
    public const double ViewportBorderThickness = 1;

    public const double FontSizeBody = 12;
    public const double FontSizeSmall = 11;
    public const double FontSizeMono = 12;

    /// <summary>The 4 / 8 / 12 / 16 spacing steps every layout is built from.</summary>
    public const double Space1 = 4;
    public const double Space2 = 8;
    public const double Space3 = 12;
    public const double Space4 = 16;

    // ── Font stacks ────────────────────────────────────────────────────────

    /// <summary>
    /// First family that exists on the platform wins. Artifakt Element is Autodesk's brand
    /// typeface; it is listed first so a machine that has it picks it up, and the system
    /// sans-serif otherwise.
    /// </summary>
    public const string UiFontStack = "Artifakt Element, SF Pro Text, Helvetica Neue, Segoe UI Variable Text, Segoe UI, Inter, Arial, sans-serif";

    public const string MonoFontStack = "SF Mono, Menlo, Cascadia Mono, Consolas, DejaVu Sans Mono, monospace";

    // ── Derivations ───────────────────────────────────────────────────────

    /// <summary>Colour of a command-line history line of the given kind.</summary>
    public static uint EntryColor(Commands.CommandEntryKind kind) => kind switch
    {
        Commands.CommandEntryKind.Echo => EntryEcho,
        Commands.CommandEntryKind.Prompt => AccentBright,
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
        ("CsViewportBorder", ViewportBorder),
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
        ("CsViewportBorderThickness", ViewportBorderThickness),
        ("CsFontSizeBody", FontSizeBody),
        ("CsFontSizeSmall", FontSizeSmall),
        ("CsFontSizeMono", FontSizeMono),
        ("CsSpace1", Space1),
        ("CsSpace2", Space2),
        ("CsSpace3", Space3),
        ("CsSpace4", Space4)
    ];
}
