using CloudScope.Rendering;

namespace CloudScope.Platform.Metal
{
    internal static class MetalRendererFactory
    {
        public static IPointCloudRenderer CreatePointCloudRenderer()
        {
#if MACOS
            return new MetalPointCloudRenderer();
#else
            return new NullPointCloudRenderer();
#endif
        }

        public static IHighlightRenderer CreateHighlightRenderer()
        {
#if MACOS
            return new MetalHighlightRenderer();
#else
            return new NullHighlightRenderer();
#endif
        }

        public static IOverlayRenderer CreateOverlayRenderer()
        {
#if MACOS
            return new MetalOverlayRenderer();
#else
            return new NullOverlayRenderer();
#endif
        }

        public static SelectionGizmoRenderers CreateSelectionGizmoRenderers()
        {
#if MACOS
            return new SelectionGizmoRenderers(new MetalBoxGizmoRenderer(), new MetalSphereGizmoRenderer(), new MetalCylinderGizmoRenderer());
#else
            var box = new NullSelectionGizmoRenderer();
            return new SelectionGizmoRenderers(box, new NullSelectionGizmoRenderer(), new NullSelectionGizmoRenderer());
#endif
        }

        public static IDepthPicker CreateDepthPicker() => new NullDepthPicker();
    }
}
