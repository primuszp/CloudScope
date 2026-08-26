namespace CloudScope.Rendering
{
    public interface IRendererFactory
    {
        IPointCloudRenderer CreatePointCloudRenderer();

        /// <summary>
        /// Creates the renderer that draws straight off an on-disk store, for clouds too large
        /// to hold in memory.
        /// </summary>
        IPointTileCloudRenderer CreateStreamingPointCloudRenderer();
        IHighlightRenderer CreateHighlightRenderer();
        IOverlayRenderer CreateOverlayRenderer();
        SelectionGizmoRenderers CreateSelectionGizmoRenderers();
        IDepthPicker CreateDepthPicker();
    }
}
