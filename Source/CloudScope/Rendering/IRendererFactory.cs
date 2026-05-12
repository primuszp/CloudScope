namespace CloudScope.Rendering
{
    public interface IRendererFactory
    {
        IPointCloudRenderer CreatePointCloudRenderer();
        IHighlightRenderer CreateHighlightRenderer();
        IOverlayRenderer CreateOverlayRenderer();
        SelectionGizmoRenderers CreateSelectionGizmoRenderers();
        IDepthPicker CreateDepthPicker();
    }
}
