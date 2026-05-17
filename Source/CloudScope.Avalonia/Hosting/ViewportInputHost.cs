using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CloudScope.Avalonia.Hosting.Input;
using CloudScope.Avalonia.Hosting.Platform;

namespace CloudScope.Avalonia.Hosting;

public sealed class ViewportInputHost : Border
{
    private readonly HostController _hostController;

    public ViewportInputHost(HostController hostController)
    {
        _hostController = hostController;
        Background = Brushes.Transparent;
        Focusable = true;
        Child = EmbeddedOpenTkNativeHostFactory.Create(hostController);

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
        ViewerKey key = AvaloniaViewerKeyMapper.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyDown(key);
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        ViewerKey key = AvaloniaViewerKeyMapper.ToViewerKey(e.Key);
        if (key == ViewerKey.Unknown)
            return;

        _hostController.ForwardKeyUp(key);
        e.Handled = true;
    }

}
