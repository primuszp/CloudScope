namespace CloudScope.Rendering
{
    public interface IRenderBackend
    {
        RenderBackendKind Kind { get; }
        IPointCloudRenderer CreatePointCloudRenderer();
        IHighlightRenderer CreateHighlightRenderer();
        IOverlayRenderer CreateOverlayRenderer();
        SelectionGizmoRenderers CreateSelectionGizmoRenderers();
        IDepthPicker CreateDepthPicker();
        void InitializeFrameState();
        void BeginFrame();
        void Resize(int width, int height);
    }
}
