using System;
using OpenTK.Graphics.OpenGL4;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>Compiles and links the small GLSL programs the OpenGL renderers use.</summary>
    internal static class OpenGlShaderCompiler
    {
        public static int CreateProgram(string vertexSource, string fragmentSource, string name)
        {
            int vertex = CompileShader(ShaderType.VertexShader, vertexSource, name);
            int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource, name);

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0)
                throw new InvalidOperationException($"Linking the {name} shader failed:\n{GL.GetProgramInfoLog(program)}");

            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
            return program;
        }

        private static int CompileShader(ShaderType type, string source, string name)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            if (compiled == 0)
                throw new InvalidOperationException($"Compiling the {name} {type} failed:\n{GL.GetShaderInfoLog(shader)}");

            return shader;
        }
    }
}
