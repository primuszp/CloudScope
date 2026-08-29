using System;
using CloudScope.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Draws polylines, loops and gizmo circles as overlap-free screen-space geometry:
    /// one instanced quad per segment plus one round-join fan per interior joint. The
    /// expansion itself lives in <see cref="JoinedLineShaderCore"/>, shared with Metal.
    /// </summary>
    internal sealed class OpenGlJoinedLineRenderer : IDisposable
    {
        private int _segmentProgram = -1;
        private int _joinProgram = -1;
        private int _segmentVao = -1;
        private int _joinVao = -1;
        private int _segmentVbo = -1;
        private int _joinVbo = -1;
        private Uniforms _segmentUniforms;
        private Uniforms _joinUniforms;
        private readonly int[] _viewport = new int[4];

        private readonly struct Uniforms(int program)
        {
            public readonly int Mvp = GL.GetUniformLocation(program, "uMVP");
            public readonly int Color = GL.GetUniformLocation(program, "uColor");
            public readonly int Viewport = GL.GetUniformLocation(program, "uViewport");
            public readonly int Width = GL.GetUniformLocation(program, "uWidth");
            public readonly int Dash = GL.GetUniformLocation(program, "uDash");
        }

        private const string SegmentVertSrc = @"#version 330 core
layout(location = 0) in vec3 aPrevious;
layout(location = 1) in vec3 aStart;
layout(location = 2) in vec3 aEnd;
layout(location = 3) in vec3 aNext;

uniform mat4 uMVP;
uniform vec2 uViewport;
uniform float uWidth;

noperspective out vec2 vCoord;
noperspective out float vDepth;
noperspective out float vDash;
flat out vec2 vLimits;
" + JoinedLineShaderCore.GlslHeader + JoinedLineShaderCore.Source + @"
void main()
{
    JoinedLineVertex expanded = joinedLineSegment(
        uMVP * vec4(aPrevious, 1.0), uMVP * vec4(aStart, 1.0),
        uMVP * vec4(aEnd, 1.0), uMVP * vec4(aNext, 1.0),
        uViewport, uWidth, gl_VertexID);
    gl_Position = expanded.position;
    vCoord = expanded.coord;
    vDepth = expanded.depth;
    vDash = expanded.dash;
    vLimits = expanded.limits;
}
";

        private const string JoinVertSrc = @"#version 330 core
layout(location = 0) in vec3 aPrevious;
layout(location = 1) in vec3 aJoint;
layout(location = 2) in vec3 aNext;

uniform mat4 uMVP;
uniform vec2 uViewport;
uniform float uWidth;

noperspective out vec2 vCoord;
noperspective out float vDepth;
noperspective out float vDash;
flat out vec2 vLimits;
" + JoinedLineShaderCore.GlslHeader + JoinedLineShaderCore.Source + @"
void main()
{
    JoinedLineVertex expanded = joinedLineJoin(
        uMVP * vec4(aPrevious, 1.0), uMVP * vec4(aJoint, 1.0), uMVP * vec4(aNext, 1.0),
        uViewport, uWidth, gl_VertexID);
    gl_Position = expanded.position;
    vCoord = expanded.coord;
    vDepth = expanded.depth;
    vDash = expanded.dash;
    vLimits = expanded.limits;
}
";

        private const string FragSrc = @"#version 330 core
uniform vec4 uColor;
uniform float uWidth;
uniform float uDash;

noperspective in vec2 vCoord;
noperspective in float vDepth;
noperspective in float vDash;
flat in vec2 vLimits;
out vec4 FragColor;
" + JoinedLineShaderCore.GlslHeader + JoinedLineShaderCore.Source + @"
void main()
{
    float alpha = uColor.a * joinedLineCoverage(vCoord, vLimits, vDepth, uWidth)
        * joinedLineDash(vDash, uDash);
    if (alpha <= 0.0) discard;
    FragColor = vec4(uColor.rgb, alpha);
}
";

        /// <summary>
        /// Draws the instance streams produced by <see cref="PolylineRenderGeometry"/>.
        /// The bound program and vertex array are restored before returning.
        /// </summary>
        public void Draw(
            float[] segments, int segmentCount,
            float[] joins, int joinCount,
            ref Matrix4 mvp, Vector4 color, float widthPixels, float dashPixels = 0f)
        {
            if (segmentCount <= 0)
                return;

            EnsureResources();
            GL.GetInteger(GetPName.CurrentProgram, out int previousProgram);
            GL.GetInteger(GetPName.VertexArrayBinding, out int previousVao);
            GL.GetInteger(GetPName.Viewport, _viewport);

            DrawStream(_segmentProgram, _segmentVao, _segmentVbo, _segmentUniforms,
                segments, segmentCount * PolylineRenderGeometry.SegmentFloats,
                PolylineRenderGeometry.VerticesPerSegment, segmentCount,
                PrimitiveType.TriangleStrip, ref mvp, color, widthPixels, dashPixels);

            if (joinCount > 0)
                DrawStream(_joinProgram, _joinVao, _joinVbo, _joinUniforms,
                    joins, joinCount * PolylineRenderGeometry.JoinFloats,
                    PolylineRenderGeometry.VerticesPerJoin, joinCount,
                    PrimitiveType.Triangles, ref mvp, color, widthPixels, dashPixels);

            GL.BindVertexArray(previousVao);
            GL.UseProgram(previousProgram);
        }

        private void DrawStream(
            int program, int vao, int vbo, Uniforms uniforms,
            float[] data, int floatCount, int verticesPerInstance, int instanceCount,
            PrimitiveType primitive, ref Matrix4 mvp, Vector4 color, float widthPixels, float dashPixels)
        {
            GL.UseProgram(program);
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, floatCount * sizeof(float), data, BufferUsageHint.DynamicDraw);

            GL.UniformMatrix4(uniforms.Mvp, false, ref mvp);
            GL.Uniform4(uniforms.Color, color.X, color.Y, color.Z, color.W);
            // uViewport is a vec2: keep these floating point so OpenTK dispatches glUniform2f.
            GL.Uniform2(uniforms.Viewport, MathF.Max(_viewport[2], 1f), MathF.Max(_viewport[3], 1f));
            GL.Uniform1(uniforms.Width, widthPixels);
            GL.Uniform1(uniforms.Dash, dashPixels);
            GL.DrawArraysInstanced(primitive, 0, verticesPerInstance, instanceCount);
        }

        /// <summary>Compiles the shaders up front, so a broken build fails at startup.</summary>
        public void EnsureResources()
        {
            if (_segmentProgram != -1)
                return;

            _segmentProgram = OpenGlShaderCompiler.CreateProgram(SegmentVertSrc, FragSrc, "joined line segment");
            _joinProgram = OpenGlShaderCompiler.CreateProgram(JoinVertSrc, FragSrc, "joined line join");
            _segmentUniforms = new Uniforms(_segmentProgram);
            _joinUniforms = new Uniforms(_joinProgram);
            _segmentVbo = GL.GenBuffer();
            _joinVbo = GL.GenBuffer();
            _segmentVao = CreateInstanceVao(_segmentVbo, PolylineRenderGeometry.SegmentFloats / 3);
            _joinVao = CreateInstanceVao(_joinVbo, PolylineRenderGeometry.JoinFloats / 3);
        }

        /// <summary>One vec3 attribute per instance element, all advanced once per instance.</summary>
        private static int CreateInstanceVao(int vbo, int attributeCount)
        {
            int vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            int stride = attributeCount * 3 * sizeof(float);
            for (int attribute = 0; attribute < attributeCount; attribute++)
            {
                GL.VertexAttribPointer(attribute, 3, VertexAttribPointerType.Float, false,
                    stride, (IntPtr)(attribute * 3 * sizeof(float)));
                GL.EnableVertexAttribArray(attribute);
                GL.VertexAttribDivisor(attribute, 1);
            }
            GL.BindVertexArray(0);
            return vao;
        }

        public void Dispose()
        {
            if (_segmentProgram != -1) { GL.DeleteProgram(_segmentProgram); _segmentProgram = -1; }
            if (_joinProgram != -1) { GL.DeleteProgram(_joinProgram); _joinProgram = -1; }
            if (_segmentVao != -1) { GL.DeleteVertexArray(_segmentVao); _segmentVao = -1; }
            if (_joinVao != -1) { GL.DeleteVertexArray(_joinVao); _joinVao = -1; }
            if (_segmentVbo != -1) { GL.DeleteBuffer(_segmentVbo); _segmentVbo = -1; }
            if (_joinVbo != -1) { GL.DeleteBuffer(_joinVbo); _joinVbo = -1; }
        }
    }
}
