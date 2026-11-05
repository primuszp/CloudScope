using System;

namespace CloudScope
{
    public interface IViewerHost : IDisposable
    {
        void LoadPointCloud(PointData[] points, float cloudRadius = 50f);
        void SetLasFilePath(string path);
        string OpenPointCloud(string path, long maxPoints = 0);
        void Run();
    }
}
