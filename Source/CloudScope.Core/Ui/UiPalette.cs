namespace CloudScope.Ui;

/// <summary>
/// CloudScope's design tokens — colours, metrics and font stacks — defined once for every
/// shell. The Avalonia resource dictionary and the ImGui style are both built from these
/// values, so the two user interfaces cannot drift into different-looking versions of the
/// same product.
///
/// The system is dark-only and reads as AutoCAD's graphite dark theme: a deep warm canvas,
/// warm graphite (slightly brown) surfaces, hairline borders, and no accent hue. Emphasis
/// (prompt text, focus, the active tool) is carried by a bright neutral grey; only the
/// Ok / Warn / Error signals are coloured. A point-cloud viewport is judged against its
/// surroundings, so the shell stays dark on every platform.
/// </summary>
public static class UiPalette
{
    // ── Colour tokens (0xRRGGBB) — warm graphite, no accent hue ─────────────
    //
    // The ramp leans slightly toward brown (red > green > blue by a few steps), the warm
    // graphite of AutoCAD's dark chrome, and sits lighter than pure charcoal so panels read
    // as material rather than holes. The chroma is kept low enough that a point cloud's own
    // colours are still judged against something close to neutral.

    /// <summary>The 3D canvas behind every viewport — the deepest tone in the system.</summary>
    public const uint ViewportBackdrop = 0x1F1D1B;

    /// <summary>Sunken wells: the command window body, popups, inset lists, palettes.</summary>
    public const uint SurfaceDeep = 0x2A2724;

    /// <summary>Default panel body: ribbon body, dialog surfaces.</summary>
    public const uint Surface = 0x33302C;

    /// <summary>Raised surfaces: palette headers, status bar, cards.</summary>
    public const uint SurfaceAlt = 0x3B3733;

    /// <summary>The unified titlebar / ribbon tab band — warm graphite.</summary>
    public const uint Graphite = 0x46413B;

    /// <summary>Pointer-over fill for buttons, rows and menu items.</summary>
    public const uint SurfaceHover = 0x4B463F;

    /// <summary>Hairline dividers and control outlines at rest.</summary>
    public const uint Border = 0x4A453F;

    /// <summary>Outline of a focused or actively raised control.</summary>
    public const uint BorderStrong = 0x645D55;

    /// <summary>Primary text — warm off-white.</summary>
    public const uint Text = 0xE6E1D9;

    /// <summary>Secondary text: labels, captions, section headers.</summary>
    public const uint TextDim = 0xA9A197;

    /// <summary>Disabled text and faint separators.</summary>
    public const uint TextFaint = 0x7A736A;

    /// <summary>Emphasis, not a hue: prompt text, active tool glyph, focus outline.</summary>
    public const uint Accent = 0xD6CEC2;

    /// <summary>Emphasis under the pointer.</summary>
    public const uint AccentBright = 0xF3EEE7;

    /// <summary>Lifted fill for a pressed or active control.</summary>
    public const uint AccentDim = 0x5A5248;

    /// <summary>Selected list row / highlighted completion candidate fill.</summary>
    public const uint SelectionFill = 0x524B43;

    /// <summary>
    /// The frame around the 3D viewport. Its own token so the visible edge of the viewport
    /// can be tuned — colour here, thickness in <see cref="ViewportBorderThickness"/> — without
    /// touching the shared hairline. The embedded GL child window is created borderless, so
    /// this is the only line around the viewport.
    /// </summary>
    public const uint ViewportBorder = Border;

    /// <summary>
    /// The edge of the viewport that currently has focus, when the drawing area is split into
    /// tiles. Bright but still neutral — which tile is active is shown by contrast, not a hue.
    /// </summary>
    public const uint ViewportBorderActive = Accent;

    public const uint Error = 0xE06C6C;
    public const uint Ok = 0x5FB57A;
    public const uint Warn = 0xE0A24E;

    // Data colours. The chrome carries no hue; these exist only for the few icons whose
    // subject is colour itself (the COLORBY modes), so a glyph can show what it will paint.
    public const uint DataRed = Error;
    public const uint DataGreen = Ok;
    public const uint DataOrange = Warn;
    public const uint DataBlue = 0x6C9EE0;
    public const uint DataYellow = 0xE0C24E;
    public const uint DataPurple = 0xB07CE0;

    /// <summary>Echoed command lines are as quiet as secondary text.</summary>
    public const uint EntryEcho = TextDim;

    /// <summary>Plain command output sits at primary-text weight.</summary>
    public const uint EntryOutput = Text;

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
    public const string UiFontStack = "Artifakt Element, SF Pro Text, Segoe UI Variable Text, Helvetica Neue, Segoe UI, Inter, Noto Sans, Arial, sans-serif";

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
