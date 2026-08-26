using System;
using CloudScope.Loading;

namespace CloudScope.Rendering
{
    public interface IPointCloudRenderer : IDisposable
    {
        int PointCount { get; }
        bool SupportsAttributeColoring { get; }
        bool CanUpdateColorSourceWithoutUpload { get; }
        void Initialize();
        void Upload(PointCloudRenderData data);
        void UpdateColorSource(ColorSource source);
        /// <summary>Draws the cloud and returns how many points actually went to the GPU.</summary>
        int Render(IRenderFrameData frameData, in PointRenderView view);
    }
}
