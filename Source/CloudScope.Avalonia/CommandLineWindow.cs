using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CloudScope.Avalonia;

/// <summary>
/// The command window when it floats free of the workspace, as AutoCAD's undocked command line
/// does: the same control, over the drawing rather than under it, translucent so the points it
/// covers stay readable.
/// </summary>
public sealed class CommandLineWindow : Window
{
    public CommandLineWindow()
    {
        Title = "Command";
        Width = 900;
        Height = 150;
        MinWidth = 420;
        MinHeight = 72;
        ShowInTaskbar = false;

        // Acrylic where the platform has it, an opaque panel where it does not — a floating
        // command line that turned into a transparent hole would be unreadable.
        TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
        Background = Brushes.Transparent;

        Content = new Panel
        {
            Children =
            {
                new ExperimentalAcrylicBorder
                {
                    Material = new ExperimentalAcrylicMaterial
                    {
                        BackgroundSource = AcrylicBackgroundSource.Digger,
                        TintColor = Colors.Black,
                        TintOpacity = 1,
                        MaterialOpacity = 0.7
                    }
                },
                Host
            }
        };
    }

    /// <summary>Where the command-line control lives while the window holds it.</summary>
    public ContentControl Host { get; } = new();

    /// <summary>Restores the position and size the window was last left at.</summary>
    public void RestoreBounds(string bounds)
    {
        string[] parts = bounds.Split(',');
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], out double x) || !double.TryParse(parts[1], out double y) ||
            !double.TryParse(parts[2], out double width) || !double.TryParse(parts[3], out double height))
            return;

        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
        Position = new PixelPoint((int)x, (int)y);
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    public string SaveBounds() => $"{Position.X},{Position.Y},{Width},{Height}";
}
