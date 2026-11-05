using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CloudScope.Avalonia;
using CloudScope.Avalonia.Controls;
using CloudScope.Ui;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
{ UseHeadlessDrawing = false }).SetupWithoutStarting();

string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "cloudscope-ui-checks");
Directory.CreateDirectory(output);
int failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "  ok  " : "FAIL  ")}{name}");
    if (!ok) failures++;
}

// Real shell, theme, ribbon, palettes and console. Replace only the native GPU viewport
// before attaching it; this deliberately checks UI layout without claiming GPU coverage.
var window = new MainWindow();
window.FindControl<ContentControl>("ViewportHost")!.Content = new Border
{ Background = App.Brush("CsViewportBackdrop") };
window.Show();
foreach ((int width, int height) in new[] { (1480, 920), (1040, 680) })
{
    window.Width = width; window.Height = height;
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = window.CaptureRenderedFrame();
    Check($"Shell renders at {width}x{height}", frame != null && frame.PixelSize.Width == width && frame.PixelSize.Height == height);
    frame?.Save(Path.Combine(output, $"workspace-{width}.png"));
    foreach (string name in new[] { "ContentGrid", "CommandLineBorder", "ViewportHost", "StatusText" })
    {
        var control = window.FindControl<Control>(name)!;
        Check($"{name} has usable bounds at {width}px", control.Bounds.Width > 0 && control.Bounds.Height > 0);
    }
}

// Check all ribbon tabs at the minimum supported width, including horizontally scrolled
// panels. The captured output lets a reviewer inspect the actual Fluent templates.
var ribbon = window.FindControl<ContentControl>("RibbonHost")!.Content as RibbonControl;
foreach (string tab in new[] { "Home", "View", "Display", "Label", "Analyze", "Output" })
{
    ribbon!.SelectTab(tab);
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = window.CaptureRenderedFrame();
    Check($"Ribbon {tab} renders", frame != null && ribbon.SelectedTabTitle == tab);
    frame?.Save(Path.Combine(output, $"ribbon-{tab.ToLowerInvariant()}.png"));
}
Check("Secondary text has 4.5:1 contrast on ribbon and hover surfaces",
    Contrast(UiPalette.TextDim, UiPalette.Graphite) >= 4.5 && Contrast(UiPalette.TextDim, UiPalette.SurfaceHover) >= 4.5);
Console.WriteLine($"UI captures: {output}");
Console.WriteLine(failures == 0 ? "ALL UI CHECKS PASSED" : $"{failures} UI CHECK(S) FAILED");
// Do not close the window: production Closing persists personal shell preferences.
return failures == 0 ? 0 : 1;

static double Contrast(uint a, uint b)
{
    static double Luminance(uint c)
    {
        static double Linear(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        return .2126 * Linear(UiPalette.R(c)) + .7152 * Linear(UiPalette.G(c)) + .0722 * Linear(UiPalette.B(c));
    }
    double x = Luminance(a), y = Luminance(b);
    return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
}
