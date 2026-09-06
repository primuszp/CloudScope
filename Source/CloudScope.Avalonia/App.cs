using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using CloudScope.Ui;

namespace CloudScope.Avalonia;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        // CloudScope is a dark-only workspace: a viewport full of point-cloud colour is
        // judged against its surroundings, so the shell stays dark on every platform.
        RequestedThemeVariant = ThemeVariant.Dark;

        RegisterDesignTokens();

        // The shared control theme is layered over Fluent and reads the tokens above.
        Styles.Add(new StyleInclude(new Uri("avares://CloudScope.Avalonia/"))
        {
            Source = new Uri("avares://CloudScope.Avalonia/Themes/Controls.axaml")
        });
    }

    /// <summary>
    /// The design-token brush registered under <paramref name="key"/> — the code-behind way
    /// of writing <c>{DynamicResource key}</c>, so a window built in C# reads the same
    /// palette as one built in XAML rather than mixing its own colours in.
    /// </summary>
    public static IBrush Brush(string key) =>
        Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out object? value) &&
        value is IBrush brush
            ? brush
            : Brushes.Transparent;

    /// <summary>
    /// Every colour, metric and font in the shell comes from <see cref="UiPalette"/>, the
    /// same source the ImGui viewer styles itself from, so the two shells cannot drift apart.
    /// Fluent's own accent-derived visuals (focus rings, checkmarks, sliders, selection) are
    /// repointed at CloudScope's neutral emphasis greys by overriding the SystemAccentColor
    /// family — the shell carries no accent hue — and the TextBox is forced onto the sunken
    /// surface in every state.
    /// </summary>
    private void RegisterDesignTokens()
    {
        foreach ((string key, uint color) in UiPalette.NamedColors)
            Resources[key] = new SolidColorBrush(ToColor(color));

        foreach ((string key, double value) in UiPalette.NamedMetrics)
            Resources[key] = value;

        Resources["CsCornerControl"] = new CornerRadius(UiPalette.RadiusControl);
        Resources["CsCornerCard"] = new CornerRadius(UiPalette.RadiusCard);

        // The viewport frame is bindable as a Thickness so a window can set BorderThickness
        // straight from the token; overrides the scalar the metrics loop registered.
        Resources["CsViewportBorderThickness"] = new Thickness(UiPalette.ViewportBorderThickness);

        Resources["CsUiFont"] = new FontFamily(UiPalette.UiFontStack);
        Resources["CsMonoFont"] = new FontFamily(UiPalette.MonoFontStack);

        Color accent = ToColor(UiPalette.Accent);
        Color accentBright = ToColor(UiPalette.AccentBright);
        Color accentDim = ToColor(UiPalette.AccentDim);
        Resources["SystemAccentColor"] = accent;
        Resources["SystemAccentColorLight1"] = accentBright;
        Resources["SystemAccentColorLight2"] = accentBright;
        Resources["SystemAccentColorLight3"] = accentBright;
        Resources["SystemAccentColorDark1"] = accentDim;
        Resources["SystemAccentColorDark2"] = accentDim;
        Resources["SystemAccentColorDark3"] = accentDim;

        var deep = new SolidColorBrush(ToColor(UiPalette.SurfaceDeep));
        var border = new SolidColorBrush(ToColor(UiPalette.Border));
        var borderStrong = new SolidColorBrush(ToColor(UiPalette.BorderStrong));
        var text = new SolidColorBrush(ToColor(UiPalette.Text));
        var textDim = new SolidColorBrush(ToColor(UiPalette.TextDim));
        var accentBrush = new SolidColorBrush(accent);

        foreach (string state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            Resources["TextControlBackground" + state] = deep;
            Resources["TextControlForeground" + state] = text;
            Resources["TextControlPlaceholderForeground" + state] = textDim;
        }

        Resources["TextControlBorderBrush"] = border;
        Resources["TextControlBorderBrushPointerOver"] = borderStrong;
        Resources["TextControlBorderBrushFocused"] = accentBrush;
        Resources["TextControlSelectionHighlightColor"] = new SolidColorBrush(ToColor(UiPalette.SelectionFill));
    }

    private static Color ToColor(uint color) =>
        Color.FromRgb(UiPalette.R(color), UiPalette.G(color), UiPalette.B(color));

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
