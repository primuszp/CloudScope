using System.Runtime.Versioning;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class MetalRenderBackend : IRenderBackend
    {
        public RenderBackendKind Kind => RenderBackendKind.Metal;

        public IPointCloudRenderer  CreatePointCloudRenderer()  => new MetalPointCloudRenderer();
        public IHighlightRenderer   CreateHighlightRenderer()   => new MetalHighlightRenderer();
        public IOverlayRenderer     CreateOverlayRenderer()     => new MetalOverlayRenderer();
        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => MetalRendererFactory.CreateSelectionGizmoRenderers();
        public IDepthPicker CreateDepthPicker() => new NullDepthPicker();

        public void InitializeFrameState() { }

        public void BeginFrame()
        {
            var view = MetalFrameContext.CurrentView;
            if (view == null) return;

            var descriptor = view.CurrentRenderPassDescriptor;
            if (descriptor.NativePtr == IntPtr.Zero) return;

            var ca = descriptor.ColorAttachments.Object(0);
            ca.LoadAction  = MTLLoadAction.Clear;
            ca.StoreAction = MTLStoreAction.Store;
            descriptor.ColorAttachments.SetObject(ca, 0);

            var da = descriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero)
            {
                da.LoadAction  = MTLLoadAction.Clear;
                da.StoreAction = MTLStoreAction.DontCare;
                da.ClearDepth  = 1.0;
            }
        }

        public void Resize(int width, int height) { }
    }
}
