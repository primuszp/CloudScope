using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CloudScope.Commands;
using CloudScope.Loading;
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
public sealed class MacOsEmbeddedMetalNativeHost : NativeControlHost, IEmbeddedOpenTkNativeHost
{
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly ManualViewerKeyboard _keyboard = new();
    private readonly Stopwatch _frameClock = new();
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

    public MacOsEmbeddedMetalNativeHost(HostController hostController)
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

        var device = MTLDevice.CreateSystemDefaultDevice();
        if (device.NativePtr == IntPtr.Zero)
            throw new InvalidOperationException("No Metal device is available.");

        _commandQueue = device.NewCommandQueue();
        MetalFrameContext.Initialize(device, _commandQueue.Value);
        _controller = new ViewerController(1280, 800, new MetalRenderBackend());
        _commands = new ViewerCommandDispatcher(_controller);

        _view = new MTKEventView(new NSRect(0, 0, 1280, 800), device)
        {
            ColorPixelFormat = MTLPixelFormat.BGRA8Unorm,
            DepthStencilPixelFormat = MTLPixelFormat.Depth32Float,
            ClearColor = new MTLClearColor { red = 0, green = 0, blue = 0, alpha = 1 },
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
        _view.OnKeyDown_ = code => ForwardKeyDown(MapMacKey(code));
        _view.OnKeyUp_ = code => ForwardKeyUp(MapMacKey(code));
    }

    private void Draw(MTKView view)
    {
        if (!_loaded || _controller == null || _commandQueue == null)
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
        MetalFrameContext.Begin(view, descriptor, drawable, commandBuffer);
        try
        {
            _controller.RenderFrame(dt);
        }
        finally
        {
            MetalFrameContext.End();
        }
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_view == null)
            return;

        int logicalWidth = Math.Max(1, (int)Math.Round(finalRect.Width));
        int logicalHeight = Math.Max(1, (int)Math.Round(finalRect.Height));
        SetNativeViewSize(_view.NativePtr, logicalWidth, logicalHeight);
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
        base.DestroyNativeControlCore(control);
    }

    public void LoadPointCloud(PointData[] points, float radius, Action? completed = null) =>
        Enqueue(() => { _controller?.LoadPointCloud(points, radius); completed?.Invoke(); });

    public void LoadPointCloud(PointCloudDataset dataset, Action? completed = null) =>
        Enqueue(() => { _controller?.LoadPointCloud(dataset); completed?.Invoke(); });

    public void ResetViewer() => Enqueue(() => _controller?.Reset());
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

    private static void SetNativeViewSize(IntPtr view, int width, int height)
    {
        ObjcMsgSendSize(view, SelSetFrameSize, new NSSize(width, height));
        ObjcMsgSendBool(view, SelSetNeedsDisplay, true);
    }

    private static ViewerKey MapMacKey(ushort code) => code switch
    {
        53 => ViewerKey.Escape, 49 => ViewerKey.Space, 36 => ViewerKey.Enter,
        56 => ViewerKey.LeftShift, 60 => ViewerKey.RightShift,
        59 => ViewerKey.LeftControl, 62 => ViewerKey.RightControl,
        12 => ViewerKey.Q, 13 => ViewerKey.W, 14 => ViewerKey.E,
        0 => ViewerKey.A, 1 => ViewerKey.S, 2 => ViewerKey.D,
        69 => ViewerKey.KeyPadAdd, 78 => ViewerKey.KeyPadSubtract,
        71 => ViewerKey.KeyPad7, 77 => ViewerKey.KeyPad3,
        65 => ViewerKey.KeyPad1, 87 => ViewerKey.KeyPad5,
        115 => ViewerKey.Home, 3 => ViewerKey.F,
        _ => ViewerKey.Unknown
    };

    private static readonly IntPtr SelSetFrameSize = sel_registerName("setFrameSize:");
    private static readonly IntPtr SelSetNeedsDisplay = sel_registerName("setNeedsDisplay:");

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selector);
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendSize(IntPtr receiver, IntPtr selector, NSSize size);
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendBool(IntPtr receiver, IntPtr selector, bool value);

    private sealed class ManualViewerKeyboard : IViewerKeyboard
    {
        private readonly HashSet<ViewerKey> _down = [];
        private readonly HashSet<ViewerKey> _pressed = [];
        public bool HasAnyKeyDown => _down.Count > 0;
        public void KeyDown(ViewerKey key) { if (_down.Add(key)) _pressed.Add(key); }
        public void KeyUp(ViewerKey key) { _down.Remove(key); _pressed.Remove(key); }
        public bool IsKeyDown(ViewerKey key) => _down.Contains(key);
        public bool IsKeyPressed(ViewerKey key) => _pressed.Remove(key);
    }
}
