using CloudScope.Loading;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

public interface ISurfaceRenderer : IDisposable
{
    void Upload(SurfaceMesh? mesh);
    void Render(IRenderFrameData frame, ref Matrix4 view, ref Matrix4 projection);
}
