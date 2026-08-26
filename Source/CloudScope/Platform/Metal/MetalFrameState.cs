using System.Runtime.Versioning;
using CloudScope.Rendering;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace CloudScope.Platform.Metal
{
    [SupportedOSPlatform("macos")]
    internal sealed class MetalFrameState : IRenderFrameData
    {
        public MetalFrameState(
            MTLRenderPassDescriptor renderPassDescriptor,
            CAMetalDrawable drawable,
            MTLCommandBuffer commandBuffer)
        {
            RenderPassDescriptor = renderPassDescriptor;
            Drawable = drawable;
            CommandBuffer = commandBuffer;
        }

        public MTLRenderPassDescriptor RenderPassDescriptor { get; }
        public CAMetalDrawable Drawable { get; }
        public MTLCommandBuffer CommandBuffer { get; }
        public MTLRenderCommandEncoder RenderCommandEncoder { get; set; }
    }
}
