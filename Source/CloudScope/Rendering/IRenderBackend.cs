namespace CloudScope.Rendering
{
    public interface IRenderBackend : IRendererFactory, IRenderFrameLifecycle
    {
        RenderBackendKind Kind { get; }
    }
}
