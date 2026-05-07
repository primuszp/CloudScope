using System.Runtime.Versioning;
using CloudScope.Platform.Metal.Rendering;
using CloudScope.Rendering;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class MetalRenderBackend : IRenderBackend
    {
        private MTLTexture _depthTexture;
        private ulong _depthWidth;
        private ulong _depthHeight;

        public RenderBackendKind Kind => RenderBackendKind.Metal;

        public IPointCloudRenderer  CreatePointCloudRenderer()  => new MetalPointCloudRenderer();
        public IHighlightRenderer   CreateHighlightRenderer()   => new MetalHighlightRenderer();
        public IOverlayRenderer     CreateOverlayRenderer()     => new MetalOverlayRenderer();
        public SelectionGizmoRenderers CreateSelectionGizmoRenderers()
            => MetalRendererFactory.CreateSelectionGizmoRenderers();
        public IDepthPicker CreateDepthPicker() => new MetalDepthPicker();

        public void InitializeFrameState() { }

        public void BeginFrame()
        {
            var descriptor = MetalFrameContext.CurrentRenderPassDescriptor;
            if (descriptor.NativePtr == IntPtr.Zero) return;

            var ca = descriptor.ColorAttachments.Object(0);
            ca.LoadAction  = MTLLoadAction.Clear;
            ca.StoreAction = MTLStoreAction.Store;
            descriptor.ColorAttachments.SetObject(ca, 0);

            var da = descriptor.DepthAttachment;
            if (da.NativePtr != IntPtr.Zero)
            {
                var sizeSource = da.Texture;
                ulong width = sizeSource.NativePtr != IntPtr.Zero ? sizeSource.Width : 0;
                ulong height = sizeSource.NativePtr != IntPtr.Zero ? sizeSource.Height : 0;
                if (width > 0 && height > 0)
                    EnsureDepthTexture(width, height);
                if (_depthTexture.NativePtr != IntPtr.Zero)
                    da.Texture = _depthTexture;

                da.LoadAction  = MTLLoadAction.Clear;
                da.StoreAction = MTLStoreAction.Store;
                da.ClearDepth  = 1.0;
            }
        }

        public void Resize(int width, int height) { }

        private void EnsureDepthTexture(ulong width, ulong height)
        {
            if (_depthTexture.NativePtr != IntPtr.Zero &&
                _depthWidth == width &&
                _depthHeight == height)
                return;

            var desc = MTLTextureDescriptor.Texture2DDescriptor(
                MTLPixelFormat.Depth32Float, width, height, mipmapped: false);
            desc.StorageMode = MTLStorageMode.Managed;
            desc.Usage = MTLTextureUsage.RenderTarget;
            _depthTexture = MetalFrameContext.Device.NewTexture(desc);
            _depthWidth = width;
            _depthHeight = height;
            MetalFrameContext.SetDepthTexture(_depthTexture);
        }
    }
}
