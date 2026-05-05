using System;
using OpenTK.Mathematics;

namespace CloudScope.Rendering
{
    public interface IPointCloudRenderer : IDisposable
    {
        int PointCount { get; }
        void Initialize();
        void Upload(PointData[] points);
        int Render(ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius);
    }
}
