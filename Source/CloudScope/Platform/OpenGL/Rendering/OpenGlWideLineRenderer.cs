using System;
using CloudScope.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Draws line lists wider than one pixel as screen-space quads, since core-profile
    /// OpenGL will not widen native lines (see <see cref="LineWidth"/>).
    /// </summary>
    /// <remarks>
    /// The segment endpoints are read straight out of the caller's vertex buffer as two
    /// instanced <c>vec3</c> attributes, so callers upload their line list exactly as they do
    /// for <see cref="PrimitiveType.Lines"/> and nothing has to be expanded on the CPU.
    /// </remarks>
    internal sealed class OpenGlWideLineRenderer : IDisposable
    {
        private const int VertexStride = 3 * sizeof(float);

        private int _program = -1;
        private int _vao = -1;
        private int _uMvp;
        private int _uColor;
        private int _uViewport;
        private int _uWidth;
        private readonly int[] _viewport = new int[4];

        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aStart;
layout(location = 1) in vec3 aEnd;

uniform mat4  uMVP;
uniform vec2  uViewport;   // framebuffer size in pixels
uniform float uWidth;      // line width in pixels

noperspective out vec2 vLineCoord;
noperspective out float vSegmentLength;

void main()
{
    vec4 clipStart = uMVP * vec4(aStart, 1.0);
    vec4 clipEnd   = uMVP * vec4(aEnd,   1.0);

    bool  atStart = gl_VertexID < 2;
    vec4  clipHere  = atStart ? clipStart : clipEnd;
    vec4  clipThere = atStart ? clipEnd   : clipStart;
    float side = (gl_VertexID % 2 == 0) ? -1.0 : 1.0;

    // Behind the eye the perspective divide is meaningless; leave that end unexpanded
    // so the segment degenerates to the thin line instead of smearing across the screen.
    if (clipHere.w <= 0.0 || clipThere.w <= 0.0)
    {
        gl_Position = clipHere;
        vLineCoord = vec2(0.0);
        vSegmentLength = 0.0;
        return;
    }

    vec2 halfViewport = uViewport * 0.5;
    vec2 screenHere  = clipHere.xy  / clipHere.w  * halfViewport;
    vec2 screenThere = clipThere.xy / clipThere.w * halfViewport;

    vec2  delta  = screenThere - screenHere;
    float length2 = dot(delta, delta);
    vec2  direction = length2 > 1e-12 ? delta * inversesqrt(length2) : vec2(1.0, 0.0);
    vec2  normal = vec2(-direction.y, direction.x);

    float projectedLength = sqrt(length2);
    // Always form a round screen-space capsule. It retains a width-sized footprint even
    // when the 3D segment is viewed end-on and its projected length approaches zero.
    float outerHalfWidth = uWidth * 0.5 + 0.5;
    float capExtension = outerHalfWidth;
    float capDirection = atStart ? -1.0 : 1.0;
    vec2 offsetNdc = (normal * side * outerHalfWidth
        + direction * capDirection * capExtension) / halfViewport;
    gl_Position = vec4(clipHere.xy + offsetNdc * clipHere.w, clipHere.z, clipHere.w);
    vLineCoord = vec2(atStart ? -outerHalfWidth : projectedLength + outerHalfWidth,
        side * outerHalfWidth);
    vSegmentLength = projectedLength;
}
";

        private const string FragSrc = @"
#version 330 core
uniform vec4 uColor;
uniform float uWidth;
noperspective in vec2 vLineCoord;
noperspective in float vSegmentLength;
out vec4 FragColor;
void main()
{
    float alongOutside = max(max(-vLineCoord.x, vLineCoord.x - vSegmentLength), 0.0);
    float distanceToSegment = length(vec2(alongOutside, vLineCoord.y));
    float halfWidth = uWidth * 0.5;
    float coverage = 1.0 - smoothstep(halfWidth - 0.5, halfWidth + 0.5, distanceToSegment);
    FragColor = vec4(uColor.rgb, uColor.a * coverage);
}
";

        /// <summary>
        /// Draws <paramref name="vertexCount"/> line-list vertices from <paramref name="vbo"/>
        /// (tightly packed <c>vec3</c>) as quads <paramref name="widthPixels"/> wide.
        /// The bound program and vertex array are restored before returning, so callers can
        /// keep their own shader state across the call.
        /// </summary>
        /// <param name="vertexStride">
        /// Byte distance between vertices in <paramref name="vbo"/>; pass the interleaved
        /// stride when the buffer carries more than the position.
        /// </param>
        public void Draw(
            int vbo,
            int firstVertex,
            int vertexCount,
            ref Matrix4 mvp,
            Vector4 color,
            float widthPixels,
            int vertexStride = VertexStride)
        {
            int segmentCount = vertexCount / 2;
            if (segmentCount <= 0)
                return;

            EnsureResources();

            GL.GetInteger(GetPName.CurrentProgram, out int previousProgram);
            GL.GetInteger(GetPName.VertexArrayBinding, out int previousVao);
            GL.GetInteger(GetPName.Viewport, _viewport);

            GL.UseProgram(_program);
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            // One instance per segment: attribute 0 is its first endpoint, attribute 1 the second.
            int segmentStride = 2 * vertexStride;
            IntPtr baseOffset = (IntPtr)(firstVertex * vertexStride);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, segmentStride, baseOffset);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, segmentStride, baseOffset + vertexStride);

            GL.UniformMatrix4(_uMvp, false, ref mvp);
            GL.Uniform4(_uColor, color.X, color.Y, color.Z, color.W);
            GL.Uniform2(_uViewport, MathF.Max(_viewport[2], 1), MathF.Max(_viewport[3], 1));
            GL.Uniform1(_uWidth, widthPixels);

            GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, segmentCount);

            GL.BindVertexArray(previousVao);
            GL.UseProgram(previousProgram);
        }

        /// <summary>Compiles the shader up front, so a broken build fails at startup.</summary>
        public void EnsureResources()
        {
            if (_program != -1)
                return;

            _program = OpenGlShaderCompiler.CreateProgram(VertSrc, FragSrc, "wide line");
            _uMvp = GL.GetUniformLocation(_program, "uMVP");
            _uColor = GL.GetUniformLocation(_program, "uColor");
            _uViewport = GL.GetUniformLocation(_program, "uViewport");
            _uWidth = GL.GetUniformLocation(_program, "uWidth");

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribDivisor(0, 1);
            GL.VertexAttribDivisor(1, 1);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_program != -1)
            {
                GL.DeleteProgram(_program);
                _program = -1;
            }
            if (_vao != -1)
            {
                GL.DeleteVertexArray(_vao);
                _vao = -1;
            }
        }
    }
}
