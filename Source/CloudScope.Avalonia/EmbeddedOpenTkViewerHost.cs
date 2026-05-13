using CloudScope.Rendering;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Collections.Concurrent;

namespace CloudScope.Avalonia;

public sealed class EmbeddedOpenTkViewerHost : OpenTkViewerHost
{
    private readonly ConcurrentQueue<Action<EmbeddedOpenTkViewerHost>> _actions = new();
    private readonly ManualViewerKeyboard _keyboard = new();
    private readonly ManualResetEventSlim _loaded = new(false);
    private int _effectiveFramebufferWidth;
    private int _effectiveFramebufferHeight;

    public EmbeddedOpenTkViewerHost(int width, int height, IRenderBackend renderBackend)
        : base(width, height, renderBackend, enableOverlay: false, startVisible: false)
    {
    }

    public void Enqueue(Action<EmbeddedOpenTkViewerHost> action) => _actions.Enqueue(action);

    public void SetKeyState(ViewerKey key, bool isDown)
    {
        if (key == ViewerKey.Unknown)
            return;

        if (isDown)
            _keyboard.KeyDown(key);
        else
            _keyboard.KeyUp(key);
    }

    public void ForwardKeyDown(ViewerKey key)
    {
        if (key == ViewerKey.Unknown)
            return;

        SetKeyState(key, true);
        bool ctrl = _keyboard.IsKeyDown(ViewerKey.LeftControl) || _keyboard.IsKeyDown(ViewerKey.RightControl);
        ForwardKeyDown(key, ctrl, ToPhysicalMouseX(), ToPhysicalMouseY());
    }

    public void PumpActions()
    {
        while (_actions.TryDequeue(out Action<EmbeddedOpenTkViewerHost>? action))
            action(this);
    }

    public void MarkEmbeddedLoaded() => _loaded.Set();

    public bool WaitUntilLoaded(TimeSpan timeout) => _loaded.Wait(timeout);

    public void InitializeEmbedded()
    {
        OnLoad();
        MarkEmbeddedLoaded();
    }

    public void PumpEmbeddedFrame(double dt)
    {
        ProcessEvents(0);
        OnUpdateFrame(new FrameEventArgs(dt));
        OnRenderFrame(new FrameEventArgs(dt));
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        _loaded.Set();
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        PumpActions();
    }

    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        SetKeyState(ToViewerKey(e.Key), true);
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyboardKeyEventArgs e)
    {
        SetKeyState(ToViewerKey(e.Key), false);
        base.OnKeyUp(e);
    }

    protected override IViewerKeyboard CreateKeyboardAdapter() => _keyboard;

    public void SyncFramebufferViewport(int? width = null, int? height = null)
    {
        int framebufferWidth = width ?? FramebufferSize.X;
        int framebufferHeight = height ?? FramebufferSize.Y;
        if (framebufferWidth > 0 && framebufferHeight > 0)
        {
            _effectiveFramebufferWidth = framebufferWidth;
            _effectiveFramebufferHeight = framebufferHeight;
            ResizeFramebuffer(framebufferWidth, framebufferHeight);
        }
    }

    protected override int EffectiveFramebufferWidth =>
        _effectiveFramebufferWidth > 0 ? _effectiveFramebufferWidth : base.EffectiveFramebufferWidth;

    protected override int EffectiveFramebufferHeight =>
        _effectiveFramebufferHeight > 0 ? _effectiveFramebufferHeight : base.EffectiveFramebufferHeight;

    private int ToPhysicalMouseX() => ToPhysicalX(MouseState.Position.X);

    private int ToPhysicalMouseY() => ToPhysicalY(MouseState.Position.Y);

    private int ToPhysicalX(float logical)
    {
        float scale = ClientSize.X > 0 ? (float)EffectiveFramebufferWidth / ClientSize.X : 1f;
        return (int)(logical * scale);
    }

    private int ToPhysicalY(float logical)
    {
        float scale = ClientSize.Y > 0 ? (float)EffectiveFramebufferHeight / ClientSize.Y : 1f;
        return (int)(logical * scale);
    }

    private sealed class ManualViewerKeyboard : IViewerKeyboard
    {
        private readonly HashSet<ViewerKey> _down = new();
        private readonly HashSet<ViewerKey> _pressed = new();

        public void KeyDown(ViewerKey key)
        {
            if (_down.Add(key))
                _pressed.Add(key);
        }

        public void KeyUp(ViewerKey key)
        {
            _down.Remove(key);
            _pressed.Remove(key);
        }

        public bool IsKeyPressed(ViewerKey key)
        {
            if (!_pressed.Contains(key))
                return false;

            _pressed.Remove(key);
            return true;
        }

        public bool IsKeyDown(ViewerKey key) => _down.Contains(key);
    }
}
