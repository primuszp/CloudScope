using System;
using OpenTK.Mathematics;
using CloudScope.Selection;

namespace CloudScope.Rendering
{
    public interface IOverlayRenderer : IDisposable
    {
        void Initialize();
        void RenderPivotIndicator(ref Matrix4 view, ref Matrix4 proj, OrbitCamera camera, Vector3 pivot, float fade, float flash);
        void RenderCenterCrosshair(int width, int height, float alpha);
        void RenderModeIndicator(int width, int height, SelectionToolType toolType);
    }
}
