using System.Runtime.Versioning;
using CloudScope.Platform.Metal.ObjC;
using CloudScope.Rendering;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalFrameState : IRenderFrameData
    {
        public MetalFrameState(
            MTKView view,
            MTLRenderPassDescriptor renderPassDescriptor,
            CAMetalDrawable drawable,
            MTLCommandBuffer commandBuffer)
        {
            View = view;
            RenderPassDescriptor = renderPassDescriptor;
            Drawable = drawable;
            CommandBuffer = commandBuffer;
        }

        public MTKView View { get; }
        public MTLRenderPassDescriptor RenderPassDescriptor { get; }
        public CAMetalDrawable Drawable { get; }
        public MTLCommandBuffer CommandBuffer { get; }
        public MTLRenderCommandEncoder RenderCommandEncoder { get; set; }
    }
}
