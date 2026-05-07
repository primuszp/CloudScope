using System.Runtime.Versioning;
using CloudScope.Rendering;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal static class MetalRendererFactory
    {
        public static SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => new SelectionGizmoRenderers(
                new MetalBoxGizmoRenderer(),
                new MetalSphereGizmoRenderer(),
                new MetalCylinderGizmoRenderer());
    }
}
