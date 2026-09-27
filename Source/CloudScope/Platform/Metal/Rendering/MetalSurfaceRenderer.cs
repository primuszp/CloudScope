using System.Runtime.Versioning;
using CloudScope.Loading;
using CloudScope.Rendering;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering;

[SupportedOSPlatform("macos")]
internal sealed class MetalSurfaceRenderer(MetalRenderContext context) : ISurfaceRenderer
{
    private readonly MetalPrimitiveRenderer _renderer = new(context);
    private MTLBuffer _buffer;
    private int _count;
    public void Upload(SurfaceMesh? mesh)
    {
        if (_buffer.NativePtr != IntPtr.Zero) { MetalResources.Release(_buffer.NativePtr); _buffer = default; }
        _count = mesh?.Indices.Length ?? 0;
        if (_count == 0) return;
        float[] data = new float[_count * 3];
        for (int i = 0; i < _count; i++)
        {
            Vector3 v = mesh!.Vertices[mesh.Indices[i]];
            data[i * 3] = v.X; data[i * 3 + 1] = v.Y; data[i * 3 + 2] = v.Z;
        }
        _buffer = _renderer.CreateStaticBuffer(data);
    }
    public void Render(IRenderFrameData frame, ref Matrix4 view, ref Matrix4 projection)
    {
        if (_count == 0 || frame is not MetalFrameState state) return;
        _renderer.SetFrame(state);
        _renderer.Draw(_buffer, _count, MTLPrimitiveType.Triangle, view * projection, new Vector4(0.4f, 0.7f, 0.8f, 1), true, writeDepth: true);
    }
    public void Dispose()
    {
        if (_buffer.NativePtr != IntPtr.Zero) MetalResources.Release(_buffer.NativePtr);
        _renderer.Dispose();
    }
}
