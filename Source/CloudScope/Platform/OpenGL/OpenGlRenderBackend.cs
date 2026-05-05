using OpenTK.Graphics.OpenGL4;

namespace CloudScope.Rendering
{
    public sealed class OpenGlRenderBackend : IRenderBackend
    {
        public RenderBackendKind Kind => RenderBackendKind.OpenGL;

        public IPointCloudRenderer CreatePointCloudRenderer() => new OpenGlPointCloudRenderer();

        public IHighlightRenderer CreateHighlightRenderer() => new OpenGlHighlightRenderer();

        public IOverlayRenderer CreateOverlayRenderer() => new OpenGlOverlayRenderer();

        public SelectionGizmoRenderers CreateSelectionGizmoRenderers() =>
            new(new OpenGlBoxGizmoRenderer(), new OpenGlSphereGizmoRenderer(), new OpenGlCylinderGizmoRenderer());

        public IDepthPicker CreateDepthPicker() => new OpenGlDepthPicker();

        public void InitializeFrameState()
        {
            GL.ClearColor(0.08f, 0.08f, 0.12f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ProgramPointSize);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        public void BeginFrame()
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public void Resize(int width, int height)
        {
            GL.Viewport(0, 0, width, height);
        }
    }
}
