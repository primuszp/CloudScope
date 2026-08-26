using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CloudScope.Commands;
using CloudScope.Loading;
using CloudScope.Platform.MacOS;
using CloudScope.Platform.MacOS.ObjC;
using CloudScope.Platform.Metal;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Ui;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;
using NSRect = CloudScope.Platform.MacOS.ObjC.NSRect;

namespace CloudScope.Avalonia.Hosting.Platform.MacOS;

/// <summary>Embeds CloudScope's Metal renderer in Avalonia through a native MTKView.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsEmbeddedMetalHost : NativeControlHost, IEmbeddedViewerHost
{
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly ViewerKeyboardState _keyboard = new();
    private readonly Stopwatch _frameClock = new();
    private MetalRenderBackend? _renderBackend;
    private ViewerController? _controller;
    private ViewerCommandDispatcher? _commands;
    private MTLCommandQueue? _commandQueue;
    private MTKEventView? _view;
    private MTKViewDelegate? _viewDelegate;
    private DispatcherTimer? _pumpTimer;
    private int _drawableWidth;
    private int _drawableHeight;
    private int _lastMouseX;
    private int _lastMouseY;
    private bool _loaded;

    public MacOsEmbeddedMetalHost(HostController hostController)
    {
        Commands = new DelegatingCommandExecutor(
            () => _commands,
            "Embedded Metal host is not ready.",
            RequestRedraw);
        hostController.SetEmbeddedHost(this);
    }

    public ViewerStatusSnapshot Status => _controller?.Status ?? ViewerStatusSnapshot.Empty;
    public string RendererName => "Metal";
    public IReadOnlyCollection<CloudScope.Labeling.LabelDefinition> LabelDefinitions =>
        _controller?.LabelRegistry.Definitions ?? [];
    public string ActiveLabel => _controller?.CurrentLabel ?? "";
    public int? ActiveInstanceId => _controller?.CurrentInstanceId;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
            return base.CreateNativeControlCore(parent);

        ObjectiveC.LinkMetal();
        ObjectiveC.LinkCoreGraphics();
        ObjectiveC.LinkAppKit();
        ObjectiveC.LinkMetalKit();

        _renderBackend = MetalRenderBackend.CreateWithSystemDefaultDevice();
        MTLDevice device = _renderBackend.Device;
        _commandQueue = device.NewCommandQueue();
        _controller = new ViewerController(1280, 800, _renderBackend);
        _commands = new ViewerCommandDispatcher(_controller);

        _view = new MTKEventView(new NSRect(0, 0, 1280, 800), device)
        {
            ColorPixelFormat = MTLPixelFormat.BGRA8Unorm,
            DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
            ClearColor = MetalClearColor.FromPalette(),
            FramebufferOnly = false,
            Paused = true,
            EnableSetNeedsDisplay = true
        };

        ConfigureViewCallbacks();
        _controller.Load();
        _loaded = true;
        _frameClock.Start();
        StartPump();
        RequestRedraw();
        return new PlatformHandle(_view.NativePtr, "NSView");
    }

    private void ConfigureViewCallbacks()
    {
        _viewDelegate = new MTKViewDelegate();
        _viewDelegate.OnDraw_ = Draw;
        _viewDelegate.OnSizeChange_ = (_, size) => ResizeDrawable((int)size.Width, (int)size.Height);
        _view!.Delegate = _viewDelegate;
        _view.OnMouseDown_ = (button, x, y) => { RememberMouse(x, y); _controller?.MouseDown(button, x, y); RequestRedraw(); };
        _view.OnMouseUp_ = (button, x, y) => { RememberMouse(x, y); _controller?.MouseUp(button, x, y); RequestRedraw(); };
        _view.OnMouseMove_ = (x, y) => { RememberMouse(x, y); _controller?.MouseMove(x, y); RequestRedraw(); };
        _view.OnMouseWheel_ = (x, y, delta) => { RememberMouse(x, y); _controller?.MouseWheel(x, y, delta); RequestRedraw(); };
        _view.OnKeyDown_ = code => ForwardKeyDown(MacKeyCodes.ToViewerKey(code));
        _view.OnKeyUp_ = code => ForwardKeyUp(MacKeyCodes.ToViewerKey(code));
    }

    private void Draw(MTKView view)
    {
        if (!_loaded || _controller == null || _commandQueue == null || _renderBackend == null)
            return;

        var descriptor = view.CurrentRenderPassDescriptor;
        var drawable = view.CurrentDrawable;
        if (descriptor.NativePtr == IntPtr.Zero || drawable.NativePtr == IntPtr.Zero)
            return;

        SyncDrawableSize(descriptor);
        float dt = (float)_frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        _controller.UpdateFrame(dt, _keyboard);

        var commandBuffer = _commandQueue.Value.CommandBuffer();
        _renderBackend!.PrepareFrame(descriptor, drawable, commandBuffer);
        // RenderFrame's frame session commits, presents and ends the frame on dispose.
        _controller.RenderFrame(dt);
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_view == null)
            return;

        int logicalWidth = Math.Max(1, (int)Math.Round(finalRect.Width));
        int logicalHeight = Math.Max(1, (int)Math.Round(finalRect.Height));
        NSViewInterop.SetFrameSize(_view.NativePtr, logicalWidth, logicalHeight);
        NSViewInterop.SetNeedsDisplay(_view.NativePtr);
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        ResizeDrawable(
            Math.Max(1, (int)Math.Round(logicalWidth * scale)),
            Math.Max(1, (int)Math.Round(logicalHeight * scale)));
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _pumpTimer?.Stop();
        _pumpTimer = null;
        _loaded = false;
        _view?.Dispose();
        _view = null;
        _viewDelegate?.Dispose();
        _viewDelegate = null;
        _controller?.Dispose();
        _controller = null;
        _commands = null;
        _renderBackend = null;
        base.DestroyNativeControlCore(control);
    }

    /// <summary>
    /// The Metal host renders on demand, so every command is followed by a redraw request —
    /// that side effect is the only reason this host wraps the dispatcher at all.
    /// </summary>
    public ICommandExecutor Commands { get; }

    public void ForwardKeyDown(ViewerKey key)
    {
        if (key == ViewerKey.Unknown || _controller == null)
            return;
        _keyboard.KeyDown(key);
        bool ctrl = _keyboard.IsKeyDown(ViewerKey.LeftControl) || _keyboard.IsKeyDown(ViewerKey.RightControl);
        if (_commands?.TryExecuteShortcut(key, ctrl) != true)
            _controller.KeyDown(key, ctrl, _lastMouseX, _lastMouseY);
        RequestRedraw();
    }

    public void ForwardKeyUp(ViewerKey key)
    {
        _keyboard.KeyUp(key);
        RequestRedraw();
    }

    public void FocusViewer() => _view?.MakeFirstResponder();

    private void Enqueue(Action action) => _actions.Enqueue(action);

    private void StartPump()
    {
        _pumpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pumpTimer.Tick += (_, _) =>
        {
            while (_actions.TryDequeue(out Action? action))
            {
                action();
                RequestRedraw();
            }
            if (_controller?.NeedsContinuousFrames == true)
                RequestRedraw();
        };
        _pumpTimer.Start();
    }

    private void RequestRedraw() => _view?.SetNeedsDisplay();
    private void RememberMouse(int x, int y) { _lastMouseX = x; _lastMouseY = y; }

    private void SyncDrawableSize(MTLRenderPassDescriptor descriptor)
    {
        var texture = descriptor.ColorAttachments.Object(0).Texture;
        if (texture.NativePtr != IntPtr.Zero)
            ResizeDrawable((int)texture.Width, (int)texture.Height);
    }

    private void ResizeDrawable(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == _drawableWidth && height == _drawableHeight))
            return;
        _drawableWidth = width;
        _drawableHeight = height;
        _view?.UpdateDrawableSize(width, height);
        _controller?.Resize(width, height);
        RequestRedraw();
    }
}
