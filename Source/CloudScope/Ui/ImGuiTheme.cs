using System;
using System.IO;
using System.Numerics;
using CloudScope.Commands;
using ImGuiNET;

namespace CloudScope.Ui
{
    /// <summary>
    /// The GameWindow shell's visual identity: a flat, CAD-like dark theme matching the
    /// Avalonia dark variant, plus DPI-aware fonts. Everything the panel-rendered UI draws
    /// gets its colours and metrics from here.
    /// </summary>
    public static class ImGuiTheme
    {
        // Same palette the Avalonia shell builds its brushes from.
        public static readonly Vector4 Accent      = Rgb(UiPalette.Accent);
        public static readonly Vector4 AccentDim   = Rgb(UiPalette.AccentDim);
        public static readonly Vector4 Surface     = Rgb(UiPalette.Surface);
        public static readonly Vector4 SurfaceAlt  = Rgb(UiPalette.SurfaceAlt);
        public static readonly Vector4 SurfaceDeep = Rgb(UiPalette.SurfaceDeep);
        public static readonly Vector4 Border      = Rgb(UiPalette.Border);
        public static readonly Vector4 Text        = Rgb(UiPalette.Text);
        public static readonly Vector4 TextDim     = Rgb(UiPalette.TextDim);
        public static readonly Vector4 Error       = Rgb(UiPalette.Error);
        public static readonly Vector4 Ok          = Rgb(UiPalette.Ok);

        private static Vector4 Rgb(uint color) =>
            new(UiPalette.R(color) / 255f, UiPalette.G(color) / 255f, UiPalette.B(color) / 255f, 1f);

        /// <summary>Monospace font used by the command window; valid once <see cref="Apply"/> has run.</summary>
        public static ImFontPtr MonoFont { get; private set; }

        /// <summary>True when a distinct monospace font was loaded and can be pushed.</summary>
        public static bool HasMonoFont { get; private set; }

        public static void Apply(float uiScale)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            LoadFonts(io, uiScale);

            ImGui.StyleColorsDark();
            ImGuiStylePtr style = ImGui.GetStyle();

            style.WindowRounding = 0f;
            style.ChildRounding = 0f;
            style.FrameRounding = 2f;
            style.GrabRounding = 2f;
            style.PopupRounding = 2f;
            style.ScrollbarRounding = 2f;
            style.WindowBorderSize = 1f;
            style.FrameBorderSize = 0f;
            style.WindowPadding = new Vector2(10f, 8f);
            style.FramePadding = new Vector2(7f, 4f);
            style.ItemSpacing = new Vector2(8f, 6f);
            style.ScrollbarSize = 12f;

            Set(ImGuiCol.WindowBg, Surface);
            Set(ImGuiCol.ChildBg, SurfaceDeep);
            Set(ImGuiCol.PopupBg, SurfaceDeep with { W = 0.98f });
            Set(ImGuiCol.Border, Border);
            Set(ImGuiCol.Text, Text);
            Set(ImGuiCol.TextDisabled, TextDim);
            Set(ImGuiCol.FrameBg, SurfaceDeep);
            Set(ImGuiCol.FrameBgHovered, new Vector4(0.176f, 0.204f, 0.235f, 1f));
            Set(ImGuiCol.FrameBgActive, new Vector4(0.204f, 0.239f, 0.275f, 1f));
            Set(ImGuiCol.TitleBg, SurfaceDeep);
            Set(ImGuiCol.TitleBgActive, SurfaceAlt);
            Set(ImGuiCol.MenuBarBg, SurfaceAlt);
            Set(ImGuiCol.Header, new Vector4(0.196f, 0.243f, 0.286f, 1f));
            Set(ImGuiCol.HeaderHovered, new Vector4(0.235f, 0.298f, 0.353f, 1f));
            Set(ImGuiCol.HeaderActive, Accent with { W = 0.55f });
            Set(ImGuiCol.Button, new Vector4(0.176f, 0.204f, 0.235f, 1f));
            Set(ImGuiCol.ButtonHovered, new Vector4(0.227f, 0.267f, 0.306f, 1f));
            Set(ImGuiCol.ButtonActive, Accent with { W = 0.65f });
            Set(ImGuiCol.CheckMark, Accent);
            Set(ImGuiCol.SliderGrab, Accent with { W = 0.8f });
            Set(ImGuiCol.SliderGrabActive, Accent);
            Set(ImGuiCol.Separator, Border);
            Set(ImGuiCol.ResizeGrip, Border with { W = 0.6f });
            Set(ImGuiCol.ResizeGripHovered, Accent with { W = 0.7f });

            style.ScaleAllSizes(uiScale);
        }

        /// <summary>Colour for a command-line history line of the given kind.</summary>
        public static Vector4 EntryColor(CommandEntryKind kind) => Rgb(UiPalette.EntryColor(kind));

        private static void Set(ImGuiCol target, Vector4 color) => ImGui.GetStyle().Colors[(int)target] = color;

        // The default 13 px bitmap font is unreadable once the UI is drawn in physical pixels,
        // so load real system fonts at the display's scale and fall back to the built-in one.
        private static void LoadFonts(ImGuiIOPtr io, float uiScale)
        {
            float uiSize = MathF.Round(14f * uiScale);
            float monoSize = MathF.Round(13f * uiScale);

            io.Fonts.Clear();
            if (!TryAddFont(io, UiFontCandidates, uiSize))
                io.Fonts.AddFontDefault();

            ImFontPtr? mono = TryGetFont(io, MonoFontCandidates, monoSize);
            HasMonoFont = mono.HasValue;
            MonoFont = mono ?? io.Fonts.Fonts[0];
        }

        private static readonly string[] UiFontCandidates =
        [
            "/System/Library/Fonts/SFNS.ttf",
            "/System/Library/Fonts/Helvetica.ttc",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        ];

        private static readonly string[] MonoFontCandidates =
        [
            "/System/Library/Fonts/SFNSMono.ttf",
            "/System/Library/Fonts/Menlo.ttc",
            @"C:\Windows\Fonts\consola.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
        ];

        private static bool TryAddFont(ImGuiIOPtr io, string[] candidates, float size) =>
            TryGetFont(io, candidates, size) != null;

        private static ImFontPtr? TryGetFont(ImGuiIOPtr io, string[] candidates, float size)
        {
            foreach (string path in candidates)
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    return io.Fonts.AddFontFromFileTTF(path, size);
                }
                catch
                {
                    // A font we cannot parse is not a reason to start without a UI.
                }
            }

            return null;
        }
    }
}
