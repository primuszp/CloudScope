using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Loading;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// The point cloud shader and its vertex layout, shared by the resident and streaming
    /// renderers.
    /// </summary>
    /// <remarks>
    /// The two differ only in where the points come from — one buffer uploaded once, or pages
    /// written as the camera moves — so the program, its uniforms, and the attribute layout
    /// are the same object in both. Keeping them here is what makes it true that a cloud looks
    /// identical whichever path drew it.
    /// </remarks>
    internal sealed class OpenGlPointCloudProgram : IDisposable
    {
        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aCol;
layout(location = 2) in float aZ;
layout(location = 3) in float aIntensity;
layout(location = 4) in float aClass;
layout(location = 5) in float aReturn;
layout(location = 6) in vec3 aRgb;

out vec3 vColor;

uniform mat4 view;
uniform mat4 projection;
uniform float pointSize;
uniform int colorSource;
uniform bool hasAttributes;
uniform vec3 classPalette[256];

vec3 gradientColor(float t)
{
    t = clamp(t, 0.0, 1.0);
    return vec3(t, min(1.0, 2.0 * min(t, 1.0 - t)), 1.0 - t);
}

vec3 heightColor(float z)
{
    z = clamp(z, 0.0, 1.0);
    return vec3(z, 1.0 - abs(2.0 * z - 1.0), 1.0 - z);
}

void main()
{
    gl_Position  = projection * view * vec4(aPos, 1.0);
    gl_PointSize = pointSize;
    if (!hasAttributes)
        vColor = aCol.rgb;
    else if (colorSource == 0)
        vColor = aRgb;
    else if (colorSource == 1)
        vColor = heightColor(aZ);
    else if (colorSource == 2)
        vColor = classPalette[int(clamp(aClass, 0.0, 255.0))];
    else if (colorSource == 3)
        vColor = gradientColor(aIntensity);
    else if (colorSource == 4)
        vColor = classPalette[int(clamp(aReturn, 0.0, 255.0))];
    else
        vColor = aCol.rgb;
}
";

        private const string FragSrc = @"
#version 330 core
in  vec3 vColor;
out vec4 FragColor;

void main()
{
    // Square points - no discard, preserves early-z and avoids
    // per-fragment branch divergence. Visually indistinguishable
    // at typical point cloud densities.
    FragColor = vec4(vColor, 1.0);
}
";

        private int _shader = -1;
        private int _uView, _uProj, _uPointSize, _uColorSource, _uHasAttributes;

        public void Initialize()
        {
            _shader = OpenGlShaderCompiler.CreateProgram(VertSrc, FragSrc, "point cloud");
            _uView = GL.GetUniformLocation(_shader, "view");
            _uProj = GL.GetUniformLocation(_shader, "projection");
            _uPointSize = GL.GetUniformLocation(_shader, "pointSize");
            _uColorSource = GL.GetUniformLocation(_shader, "colorSource");
            _uHasAttributes = GL.GetUniformLocation(_shader, "hasAttributes");
            UploadClassPalette();
        }

        /// <summary>Binds the program and sets everything that changes per frame.</summary>
        public void Use(in PointRenderView renderView, ColorSource colorSource, bool hasAttributes)
        {
            Matrix4 view = renderView.View;
            Matrix4 projection = renderView.Projection;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProj, false, ref projection);
            GL.Uniform1(_uPointSize, renderView.PointSize);
            GL.Uniform1(_uColorSource, PointRenderAttributeBuilder.MapColorSource(colorSource));
            GL.Uniform1(_uHasAttributes, hasAttributes ? 1 : 0);
        }

        /// <summary>Points the vertex attributes at the currently bound vertex buffer.</summary>
        public static void BindPositionAttributes()
        {
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, GpuPointVertex.Stride, 0);
            GL.EnableVertexAttribArray(0);
            // Normalized unsigned bytes: the shader still sees a 0..1 color.
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, GpuPointVertex.Stride, 12);
            GL.EnableVertexAttribArray(1);
        }

        /// <summary>Points the attribute inputs at the currently bound attribute buffer.</summary>
        /// <remarks>
        /// Height and intensity are normalized 16-bit; class and return number are raw byte
        /// codes, so they are not normalized; the source color is normalized bytes.
        /// </remarks>
        public static void BindAttributeAttributes()
        {
            const int Stride = GpuPointAttribute.Stride;
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.UnsignedShort, true, Stride, 0);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.UnsignedShort, true, Stride, 2);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(4, 1, VertexAttribPointerType.UnsignedByte, false, Stride, 8);
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(5, 1, VertexAttribPointerType.UnsignedByte, false, Stride, 9);
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(6, 3, VertexAttribPointerType.UnsignedByte, true, Stride, 4);
            GL.EnableVertexAttribArray(6);
        }

        public void Dispose()
        {
            if (_shader != -1)
            {
                GL.DeleteProgram(_shader);
                _shader = -1;
            }
        }

        private void UploadClassPalette()
        {
            GL.UseProgram(_shader);
            for (int i = 0; i < 256; i++)
            {
                int location = GL.GetUniformLocation(_shader, $"classPalette[{i}]");
                if (location < 0)
                    continue;

                Vector3 color = ClassColorPalette.GetColor((byte)i);
                GL.Uniform3(location, color);
            }
        }
    }
}
