using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

        // Brushes and fonts come from UiPalette, the same source the ImGui viewer styles
        // itself from, so the two shells cannot drift apart visually.
        foreach ((string key, uint color) in UiPalette.NamedColors)
            Resources[key] = new SolidColorBrush(Color.FromRgb(UiPalette.R(color), UiPalette.G(color), UiPalette.B(color)));

        Resources["CsUiFont"] = new FontFamily(UiPalette.UiFontStack);
        Resources["CsMonoFont"] = new FontFamily(UiPalette.MonoFontStack);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
