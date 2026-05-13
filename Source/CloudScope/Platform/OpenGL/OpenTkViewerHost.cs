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
        private ImGuiController? _imgui;
        private CommandLineOverlay? _commandLine;

        /// <summary>
        /// Scale factor from logical pixels (GLFW/OS) to physical pixels (framebuffer).
        /// On Retina/HiDPI displays this is 2.0; on standard displays it is 1.0.
        /// All mouse coordinates from OpenTK are in logical pixels and must be
        /// multiplied by this before being passed to the renderer.
        /// </summary>
        private float PixelScale =>
            ClientSize.X > 0 ? (float)FramebufferSize.X / ClientSize.X : 1f;

        private int ToPhysical(float logical) => (int)(logical * PixelScale);

        public OpenTkViewerHost(int width, int height)
            : this(width, height, RenderBackendFactory.CreateDefault())
        {
        }

        public OpenTkViewerHost(int width, int height, IRenderBackend renderBackend)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                ClientSize = new Vector2i(width, height),
                Title = "CloudScope - Point Cloud Viewer",
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
            })
        {
            _controller = new ViewerController(width, height, renderBackend);
        }

        public void LoadPointCloud(PointData[] points, float cloudRadius = 50f) =>
            _controller.LoadPointCloud(points, cloudRadius);

        public void SetLasFilePath(string path) => _controller.SetLasFilePath(path);

        protected override void OnLoad()
        {
            base.OnLoad();
            _controller.Load();
            _imgui = new ImGuiController(ClientSize.X, ClientSize.Y);
            _commandLine = new CommandLineOverlay(_controller);
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            if (_controller.UpdateFrame((float)args.Time, new OpenTkKeyboardAdapter(KeyboardState)))
                Close();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (_imgui?.WantsMouse == true)
                return;

            _controller.MouseDown(ToViewerButton(e.Button),
                ToPhysical(MouseState.Position.X), ToPhysical(MouseState.Position.Y));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (_imgui?.WantsMouse == true)
                return;

            _controller.MouseUp(ToViewerButton(e.Button),
                ToPhysical(MouseState.Position.X), ToPhysical(MouseState.Position.Y));
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            if (_imgui?.WantsMouse == true)
                return;

            _controller.MouseMove(ToPhysical(e.X), ToPhysical(e.Y));
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_imgui?.WantsMouse == true)
                return;

            _controller.MouseWheel(
                ToPhysical(MouseState.Position.X), ToPhysical(MouseState.Position.Y), e.OffsetY);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_imgui?.WantsKeyboard == true)
                return;

            bool ctrl = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);
            _controller.KeyDown(ToViewerKey(e.Key), ctrl,
                ToPhysical(MouseState.Position.X), ToPhysical(MouseState.Position.Y));
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            _imgui?.PressChar((uint)e.Unicode);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            _controller.RenderFrame(args.Time);
            if (_imgui != null && _commandLine != null)
            {
                _imgui.Update(this, (float)args.Time);
                _commandLine.Render(ClientSize.X, ClientSize.Y);
                _imgui.Render();
            }
            SwapBuffers();
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            // Use FramebufferSize for the GL viewport — on HiDPI/Retina displays
            // the framebuffer resolution differs from the logical window size.
            _controller.Resize(FramebufferSize.X, FramebufferSize.Y);
        }

        protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
        {
            base.OnFramebufferResize(e);
            _controller.Resize(e.Width, e.Height);
        }

        protected override void OnUnload()
        {
            _imgui?.Dispose();
            _controller.Dispose();
            base.OnUnload();
        }

        private static ViewerMouseButton ToViewerButton(MouseButton button) => button switch
        {
            MouseButton.Left => ViewerMouseButton.Left,
            MouseButton.Right => ViewerMouseButton.Right,
            MouseButton.Middle => ViewerMouseButton.Middle,
            _ => ViewerMouseButton.Left
        };

        private static ViewerKey ToViewerKey(Keys key)
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
