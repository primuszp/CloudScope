using System;
using CloudScope.Loading;
using OpenTK.Mathematics;

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
        int Render(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius);
    }
}
