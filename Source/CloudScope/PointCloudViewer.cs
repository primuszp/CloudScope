using CloudScope.Rendering;

namespace CloudScope
{
    /// <summary>
    /// Compatibility facade for the current OpenTK host.
    /// New platform work should target host-specific classes such as
    /// <see cref="OpenTkViewerHost"/> or a future MTKView-backed host.
    /// </summary>
    public sealed class PointCloudViewer : OpenTkViewerHost, IViewerHost
    {
        public PointCloudViewer(int width, int height)
            : base(width, height)
        {
        }

        public PointCloudViewer(int width, int height, IRenderBackend renderBackend)
            : base(width, height, renderBackend)
        {
        }
    }
}
