using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CloudScope.Avalonia.OpenGlHostTest;

public sealed class ViewportInputHost : Border
{
    private Point _lastPointer;
    private bool _leftDragging;
    private bool _rightDragging;

    public ViewportInputHost(HostController hostController)
    {
        Focusable = true;
        Background = Brushes.Transparent;
        Child = new AvaloniaOpenGlHostControl
        {
            HostController = hostController,
            Focusable = false
        };

        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerMoved += OnPointerMoved;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        PointerPoint point = e.GetCurrentPoint(this);
        _lastPointer = point.Position;
        _leftDragging = point.Properties.IsLeftButtonPressed;
        _rightDragging = point.Properties.IsRightButtonPressed;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _leftDragging = false;
        _rightDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        Point current = point.Position;
        Vector delta = current - _lastPointer;
        _lastPointer = current;

        if (_leftDragging)
        {
            GetHostController()?.Orbit((float)delta.X, (float)delta.Y);
            e.Handled = true;
        }
        else if (_rightDragging)
        {
            GetHostController()?.Pan((float)delta.X, (float)delta.Y, Math.Max(1, (int)Bounds.Height));
            e.Handled = true;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        GetHostController()?.Zoom((float)e.Delta.Y);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        HostController? hostController = GetHostController();
        if (hostController == null)
            return;

        if (e.Key == Key.Home)
        {
            hostController.ResetCamera();
            e.Handled = true;
        }
        else if (e.Key == Key.Add || e.Key == Key.OemPlus)
        {
            hostController.AdjustPointSize(0.5f);
            e.Handled = true;
        }
        else if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
        {
            hostController.AdjustPointSize(-0.5f);
            e.Handled = true;
        }
    }

    private HostController? GetHostController()
        => Child is AvaloniaOpenGlHostControl viewport ? viewport.HostController : null;
}
