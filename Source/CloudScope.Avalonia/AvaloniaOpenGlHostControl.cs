using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CloudScope.Avalonia;

public sealed class AvaloniaOpenGlHostControl : Border
{
    public AvaloniaOpenGlHostControl()
    {
        Child = new TextBlock
        {
            Text = "Embedded OpenTK NativeControlHost is currently implemented for Windows and macOS.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
    }
}
