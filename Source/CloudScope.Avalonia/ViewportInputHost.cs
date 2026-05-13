using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CloudScope.Avalonia;

public sealed class ViewportInputHost : Border
{
    private readonly HostController _hostController;

    public ViewportInputHost(HostController hostController)
    {
        _hostController = hostController;
        Background = Brushes.Transparent;
        Focusable = true;
        Child = OperatingSystem.IsWindows()
            ? new Win32EmbeddedOpenTkNativeHost(hostController)
            : new AvaloniaOpenGlHostControl();

        PointerPressed += (_, _) => Focus();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    public void FocusViewer()
    {
        Focus();
        _hostController.FocusViewer();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        ViewerKey key = ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyDown(key);
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        ViewerKey key = ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyUp(key);
        e.Handled = true;
    }

    public static ViewerKey ToViewerKey(Key key) => key switch
    {
        Key.Escape => ViewerKey.Escape,
        Key.Add => ViewerKey.KeyPadAdd,
        Key.Subtract => ViewerKey.KeyPadSubtract,
        Key.NumPad1 => ViewerKey.KeyPad1,
        Key.NumPad3 => ViewerKey.KeyPad3,
        Key.NumPad5 => ViewerKey.KeyPad5,
        Key.NumPad7 => ViewerKey.KeyPad7,
        Key.Home => ViewerKey.Home,
        Key.LeftShift => ViewerKey.LeftShift,
        Key.RightShift => ViewerKey.RightShift,
        Key.W => ViewerKey.W,
        Key.A => ViewerKey.A,
        Key.S => ViewerKey.S,
        Key.D => ViewerKey.D,
        Key.Q => ViewerKey.Q,
        Key.E => ViewerKey.E,
        Key.O => ViewerKey.O,
        Key.L => ViewerKey.L,
        Key.Enter => ViewerKey.Enter,
        Key.G => ViewerKey.G,
        Key.R => ViewerKey.R,
        Key.X => ViewerKey.X,
        Key.Y => ViewerKey.Y,
        Key.Z => ViewerKey.Z,
        Key.D1 => ViewerKey.D1,
        Key.D2 => ViewerKey.D2,
        Key.D3 => ViewerKey.D3,
        Key.D4 => ViewerKey.D4,
        Key.D5 => ViewerKey.D5,
        Key.D6 => ViewerKey.D6,
        Key.D7 => ViewerKey.D7,
        Key.D8 => ViewerKey.D8,
        Key.D9 => ViewerKey.D9,
        Key.Space => ViewerKey.Space,
        Key.F => ViewerKey.F,
        Key.LeftCtrl => ViewerKey.LeftControl,
        Key.RightCtrl => ViewerKey.RightControl,
        _ => ViewerKey.Unknown
    };
}
