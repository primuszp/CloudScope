using System;
using OpenTK.Mathematics;

namespace CloudScope.Rendering
{
    public interface IPointCloudRenderer : IDisposable
    {
        int PointCount { get; }
        void Initialize();
        void Upload(PointCloudRenderData data);
        int Render(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius);
    }
}
