using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using CloudScope.Platform.OpenGL;
using CloudScope.Rendering;
using CloudScope.Ui;

namespace CloudScope
{
    /// <summary>
    /// OpenTK/GameWindow host. It adapts OpenTK lifecycle and input events to
    /// <see cref="ViewerController"/>; viewer state stays outside the platform host.
    /// </summary>
    public class OpenTkViewerHost : GameWindow
    {
        private readonly ViewerController _controller;
        private readonly bool _enableOverlay;
        private readonly ViewerCommandDispatcher _commandDispatcher;
#if ENABLE_IMGUI
        private ImGuiController? _imgui;
        private CommandLineOverlay? _commandLine;
#endif

        /// <summary>
        /// Scale factor from logical pixels (GLFW/OS) to physical pixels (framebuffer).
        /// On Retina/HiDPI displays this is 2.0; on standard displays it is 1.0.
        /// All mouse coordinates from OpenTK are in logical pixels and must be
        /// multiplied by this before being passed to the renderer.
        /// </summary>
        private float PixelScaleX =>
            ClientSize.X > 0 ? (float)FramebufferSize.X / ClientSize.X : 1f;

        private float PixelScaleY =>
            ClientSize.Y > 0 ? (float)FramebufferSize.Y / ClientSize.Y : 1f;

        private int ToPhysicalX(float logical) => (int)(logical * PixelScaleX);

        private int ToPhysicalY(float logical) => (int)(logical * PixelScaleY);

        public OpenTkViewerHost(int width, int height)
            : this(width, height, RenderBackendFactory.CreateDefault())
        {
        }

        public OpenTkViewerHost(int width, int height, IRenderBackend renderBackend, bool enableOverlay = true, bool startVisible = true)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height),
                Title = "CloudScope - Point Cloud Viewer",
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                StartVisible = startVisible,
            })
        {
            _controller = new ViewerController(width, height, renderBackend);
            _commandDispatcher = new ViewerCommandDispatcher(_controller);
            _enableOverlay = enableOverlay;
        }

        public void LoadPointCloud(PointData[] points, float cloudRadius = 50f) =>
            _controller.LoadPointCloud(points, cloudRadius);

        public void SetLasFilePath(string path) => _controller.SetLasFilePath(path);

        public string ExecuteCommand(string commandText) => _commandDispatcher.Execute(commandText);

        public void ForwardKeyDown(ViewerKey key, bool ctrl, int mouseX, int mouseY) =>
            _controller.KeyDown(key, ctrl, mouseX, mouseY);

        protected override void OnLoad()
        {
            base.OnLoad();
            _controller.Load();
#if ENABLE_IMGUI
            if (_enableOverlay)
            {
                _imgui = new ImGuiController(ClientSize.X, ClientSize.Y);
                _commandLine = new CommandLineOverlay(_controller);
            }
#endif
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            if (_controller.UpdateFrame((float)args.Time, CreateKeyboardAdapter()))
                Close();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
#if ENABLE_IMGUI
            if (_imgui?.WantsMouse == true)
                return;
#endif

            _controller.MouseDown(ToViewerButton(e.Button),
                ToPhysicalX(MouseState.Position.X), ToPhysicalY(MouseState.Position.Y));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
#if ENABLE_IMGUI
            if (_imgui?.WantsMouse == true)
                return;
#endif

            _controller.MouseUp(ToViewerButton(e.Button),
                ToPhysicalX(MouseState.Position.X), ToPhysicalY(MouseState.Position.Y));
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
#if ENABLE_IMGUI
            if (_imgui?.WantsMouse == true)
                return;
#endif

            _controller.MouseMove(ToPhysicalX(e.X), ToPhysicalY(e.Y));
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
#if ENABLE_IMGUI
            if (_imgui?.WantsMouse == true)
                return;
#endif

            _controller.MouseWheel(
                ToPhysicalX(MouseState.Position.X), ToPhysicalY(MouseState.Position.Y), e.OffsetY);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
#if ENABLE_IMGUI
            if (_imgui?.WantsKeyboard == true)
                return;
#endif

            bool ctrl = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);
            _controller.KeyDown(ToViewerKey(e.Key), ctrl,
                ToPhysicalX(MouseState.Position.X), ToPhysicalY(MouseState.Position.Y));
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
#if ENABLE_IMGUI
            _imgui?.PressChar((uint)e.Unicode);
#endif
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            _controller.RenderFrame(args.Time);
#if ENABLE_IMGUI
            if (_imgui != null && _commandLine != null)
            {
                _imgui.Update(this, (float)args.Time);
                _commandLine.Render(ClientSize.X, ClientSize.Y);
                _imgui.Render();
            }
#endif
            SwapBuffers();
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            // Use FramebufferSize for the GL viewport — on HiDPI/Retina displays
            // the framebuffer resolution differs from the logical window size.
            ResizeFramebuffer(FramebufferSize.X, FramebufferSize.Y);
        }

        protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
        {
            base.OnFramebufferResize(e);
            ResizeFramebuffer(e.Width, e.Height);
        }

        protected void ResizeFramebuffer(int width, int height) => _controller.Resize(width, height);

        protected override void OnUnload()
        {
#if ENABLE_IMGUI
            _imgui?.Dispose();
#endif
            _controller.Dispose();
            base.OnUnload();
        }

        protected virtual IViewerKeyboard CreateKeyboardAdapter() => new OpenTkKeyboardAdapter(KeyboardState);

        private static ViewerMouseButton ToViewerButton(MouseButton button) => button switch
        {
            MouseButton.Left => ViewerMouseButton.Left,
            MouseButton.Right => ViewerMouseButton.Right,
            MouseButton.Middle => ViewerMouseButton.Middle,
            _ => ViewerMouseButton.Left
        };

        protected static ViewerKey ToViewerKey(Keys key)
        {
            foreach (ViewerKey viewerKey in Enum.GetValues<ViewerKey>())
            {
                if (OpenTkKeyboardAdapter.ToOpenTkKey(viewerKey) == key)
                    return viewerKey;
            }

            return ViewerKey.Unknown;
        }
    }
}
