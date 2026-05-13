using System.Collections.Concurrent;
using System.Collections.Generic;
using CloudScope.Platform.OpenGL;
using CloudScope.Rendering;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CloudScope.Avalonia;

public sealed class EmbeddedOpenTkViewerHost : OpenTkViewerHost
{
    private readonly ConcurrentQueue<Action<EmbeddedOpenTkViewerHost>> _actions = new();
    private readonly ManualViewerKeyboard _keyboard = new();
    private readonly ManualResetEventSlim _loaded = new(false);

    public EmbeddedOpenTkViewerHost(int width, int height, IRenderBackend renderBackend)
        : base(width, height, renderBackend, enableOverlay: false)
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

    private int ToPhysicalMouseX() => ToPhysical(MouseState.Position.X);

    private int ToPhysicalMouseY() => ToPhysical(MouseState.Position.Y);

    private int ToPhysical(float logical)
    {
        float scale = ClientSize.X > 0 ? (float)FramebufferSize.X / ClientSize.X : 1f;
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
