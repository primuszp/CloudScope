namespace CloudScope.Ui;

/// <summary>
/// CloudScope's colours and font stacks, defined once for every shell. The Avalonia
/// resource dictionary and the ImGui style are both built from these values, so the two
/// user interfaces cannot drift into different-looking versions of the same product.
/// </summary>
public static class UiPalette
{
    // 0xRRGGBB. The workspace is dark-only: point-cloud colour is judged against it.
    public const uint Accent = 0x4FB8E6;
    public const uint AccentDim = 0x2585B8;
    public const uint Surface = 0x252A2F;
    public const uint SurfaceAlt = 0x2B323A;
    public const uint SurfaceDeep = 0x161A1D;
    public const uint ViewportBackdrop = 0x11171C;
    public const uint Border = 0x44505A;
    public const uint Text = 0xE0E6EC;
    public const uint TextDim = 0x98A5B0;
    public const uint Error = 0xEF7B7B;
    public const uint Ok = 0x72D391;

    public const uint EntryEcho = 0xB8C4CE;
    public const uint EntryOutput = 0xD8DFE6;

    /// <summary>First family that exists on the platform wins.</summary>
    public const string UiFontStack = "SF Pro Text, Helvetica Neue, Segoe UI Variable Text, Segoe UI, Inter, sans-serif";

    public const string MonoFontStack = "SF Mono, Menlo, Cascadia Mono, Consolas, DejaVu Sans Mono, monospace";

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
        ("CsAccentDim", AccentDim),
        ("CsSurface", Surface),
        ("CsSurfaceAlt", SurfaceAlt),
        ("CsSurfaceDeep", SurfaceDeep),
        ("CsViewportBackdrop", ViewportBackdrop),
        ("CsBorder", Border),
        ("CsText", Text),
        ("CsTextDim", TextDim),
        ("CsError", Error),
        ("CsOk", Ok)
    ];
}
