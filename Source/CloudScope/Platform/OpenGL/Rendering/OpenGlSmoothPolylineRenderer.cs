using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Draws a connected screen-space ribbon from vertices carrying previous, current and
    /// next positions. Unlike a list of expanded line segments, its mitered joins have no
    /// seams or transparent-overlap bands, which is essential for gizmo circles.
    /// </summary>
    internal sealed class OpenGlSmoothPolylineRenderer : IDisposable
    {
        private const int VertexStride = 9 * sizeof(float);

        private int _program = -1;
        private int _vao = -1;
        private int _uMvp;
        private int _uColor;
        private int _uViewport;
        private int _uWidth;
        private readonly int[] _viewport = new int[4];

        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPrevious;
layout(location = 1) in vec3 aCurrent;
layout(location = 2) in vec3 aNext;

uniform mat4 uMVP;
uniform vec2 uViewport;
uniform float uWidth;

noperspective out float vDistance;

void main()
{
    vec4 previous = uMVP * vec4(aPrevious, 1.0);
    vec4 current  = uMVP * vec4(aCurrent,  1.0);
    vec4 next     = uMVP * vec4(aNext,     1.0);
    float side = (gl_VertexID % 2 == 0) ? -1.0 : 1.0;

    if (previous.w <= 0.0 || current.w <= 0.0 || next.w <= 0.0)
    {
        gl_Position = current;
        vDistance = 0.0;
        return;
    }

    vec2 halfViewport = max(uViewport, vec2(1.0)) * 0.5;
    vec2 p0 = previous.xy / previous.w * halfViewport;
    vec2 p1 = current.xy  / current.w  * halfViewport;
    vec2 p2 = next.xy     / next.w     * halfViewport;
    vec2 incomingDelta = p1 - p0;
    vec2 outgoingDelta = p2 - p1;
    bool hasIncoming = dot(incomingDelta, incomingDelta) > 1e-6;
    bool hasOutgoing = dot(outgoingDelta, outgoingDelta) > 1e-6;
    vec2 incoming = hasIncoming
        ? normalize(incomingDelta)
        : hasOutgoing ? normalize(outgoingDelta) : vec2(1.0, 0.0);
    vec2 outgoing = hasOutgoing ? normalize(outgoingDelta) : incoming;
    vec2 tangentSum = incoming + outgoing;
    vec2 tangent = dot(tangentSum, tangentSum) > 1e-6
        ? normalize(tangentSum) : outgoing;
    vec2 miter = vec2(-tangent.y, tangent.x);
    vec2 outgoingNormal = vec2(-outgoing.y, outgoing.x);
    float miterScale = min(1.0 / max(abs(dot(miter, outgoingNormal)), 0.25), 4.0);

    // The half-pixel transparent fringe provides analytic edge coverage; its geometry is
    // continuous across joins, so a ring cannot become dotted at individual segments.
    float outerHalfWidth = uWidth * 0.5 + 0.5;
    vec3 n0 = previous.xyz / previous.w;
    vec3 n1 = current.xyz / current.w;
    vec3 n2 = next.xyz / next.w;
    bool startCap = dot(n1 - n0, n1 - n0) < 1e-12;
    bool endCap = dot(n2 - n1, n2 - n1) < 1e-12;
    vec2 capOffset = startCap ? -outgoing * outerHalfWidth
        : endCap ? incoming * outerHalfWidth : vec2(0.0);
    vec2 offsetNdc = (miter * side * outerHalfWidth * miterScale + capOffset) / halfViewport;
    gl_Position = vec4(current.xy + offsetNdc * current.w, current.z, current.w);
    vDistance = side * outerHalfWidth;
}
";

        private const string FragSrc = @"
#version 330 core
uniform vec4 uColor;
uniform float uWidth;
noperspective in float vDistance;
out vec4 FragColor;
void main()
{
    float halfWidth = uWidth * 0.5;
    float coverage = 1.0 - smoothstep(halfWidth - 0.5, halfWidth + 0.5, abs(vDistance));
    FragColor = vec4(uColor.rgb, uColor.a * coverage);
}
";

        public void Draw(int vbo, int firstVertex, int vertexCount, ref Matrix4 mvp, Vector4 color, float widthPixels)
        {
            if (vbo == -1 || vertexCount < 4)
                return;

            EnsureResources();
            GL.GetInteger(GetPName.CurrentProgram, out int previousProgram);
            GL.GetInteger(GetPName.VertexArrayBinding, out int previousVao);
            GL.GetInteger(GetPName.Viewport, _viewport);

            GL.UseProgram(_program);
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VertexStride, IntPtr.Zero);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, VertexStride, (IntPtr)(3 * sizeof(float)));
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, VertexStride, (IntPtr)(6 * sizeof(float)));

            GL.UniformMatrix4(_uMvp, false, ref mvp);
            GL.Uniform4(_uColor, color.X, color.Y, color.Z, color.W);
            // uViewport is a GLSL vec2. Keep these arguments floating-point so OpenTK
            // dispatches glUniform2f; the integer overload is a GL type error and leaves
            // the uniform at (0,0), expanding a pixel-wide ribbon across the whole screen.
            GL.Uniform2(_uViewport, MathF.Max(_viewport[2], 1f), MathF.Max(_viewport[3], 1f));
            GL.Uniform1(_uWidth, widthPixels);
            GL.DrawArrays(PrimitiveType.TriangleStrip, firstVertex, vertexCount);

            GL.BindVertexArray(previousVao);
            GL.UseProgram(previousProgram);
        }

        private void EnsureResources()
        {
            if (_program != -1)
                return;

            _program = OpenGlShaderCompiler.CreateProgram(VertSrc, FragSrc, "smooth polyline");
            _uMvp = GL.GetUniformLocation(_program, "uMVP");
            _uColor = GL.GetUniformLocation(_program, "uColor");
            _uViewport = GL.GetUniformLocation(_program, "uViewport");
            _uWidth = GL.GetUniformLocation(_program, "uWidth");
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.EnableVertexAttribArray(2);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_program != -1) { GL.DeleteProgram(_program); _program = -1; }
            if (_vao != -1) { GL.DeleteVertexArray(_vao); _vao = -1; }
        }
    }
}
