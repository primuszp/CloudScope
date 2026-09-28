using CloudScope.Loading;
using CloudScope.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace CloudScope.Platform.OpenGL.Rendering;

internal sealed class OpenGlSurfaceRenderer : ISurfaceRenderer
{
    private int _program, _vao, _buffer, _count;
    public void Upload(SurfaceMesh? mesh)
    {
        _count = mesh?.Indices.Length ?? 0;
        if (_count == 0) return;
        if (_program == 0)
        {
            _program = OpenGlShaderCompiler.CreateProgram("""
                #version 330 core
                layout(location=0) in vec3 position;
                uniform mat4 mvp;
                void main() { gl_Position = mvp * vec4(position, 1); }
                """, """
                #version 330 core
                out vec4 color;
                void main() { color = vec4(0.4, 0.7, 0.8, 1); }
                """, "surface");
            _vao = GL.GenVertexArray(); _buffer = GL.GenBuffer();
        }
        float[] data = new float[_count * 3];
        for (int i = 0; i < _count; i++)
        {
            Vector3 v = mesh!.Vertices[mesh.Indices[i]];
            data[i * 3] = v.X; data[i * 3 + 1] = v.Y; data[i * 3 + 2] = v.Z;
        }
        GL.BindVertexArray(_vao); GL.BindBuffer(BufferTarget.ArrayBuffer, _buffer);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0); GL.BindVertexArray(0);
    }
    public void Render(IRenderFrameData frame, ref Matrix4 view, ref Matrix4 projection)
    {
        if (_count == 0) return;
        GL.Enable(EnableCap.DepthTest); GL.DepthMask(true);
        GL.Disable(EnableCap.CullFace);
        GL.UseProgram(_program);
        Matrix4 mvp = view * projection;
        GL.UniformMatrix4(GL.GetUniformLocation(_program, "mvp"), false, ref mvp);
        GL.BindVertexArray(_vao); GL.DrawArrays(PrimitiveType.Triangles, 0, _count); GL.BindVertexArray(0);
    }
    public void Dispose()
    {
        if (_program != 0) GL.DeleteProgram(_program);
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        if (_buffer != 0) GL.DeleteBuffer(_buffer);
    }
}
