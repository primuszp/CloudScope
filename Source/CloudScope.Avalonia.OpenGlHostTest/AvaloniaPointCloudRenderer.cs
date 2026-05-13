using System.Runtime.InteropServices;
using CloudScope;

namespace CloudScope.Avalonia.OpenGlHostTest;

internal sealed class AvaloniaPointCloudRenderer : IDisposable
{
    private const string DesktopVertexShader = """
        #version 120
        attribute vec3 aPos;
        attribute vec3 aCol;

        uniform mat4 uMvp;
        uniform float uPointSize;

        varying vec3 vColor;

        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            gl_PointSize = uPointSize;
            vColor = aCol;
        }
        """;

    private const string DesktopFragmentShader = """
        #version 120
        varying vec3 vColor;

        void main()
        {
            gl_FragColor = vec4(vColor, 1.0);
        }
        """;

    private const string EsVertexShader = """
        #version 100
        attribute vec3 aPos;
        attribute vec3 aCol;

        uniform mat4 uMvp;
        uniform float uPointSize;

        varying vec3 vColor;

        void main()
        {
            gl_Position = uMvp * vec4(aPos, 1.0);
            gl_PointSize = uPointSize;
            vColor = aCol;
        }
        """;

    private const string EsFragmentShader = """
        #version 100
        precision mediump float;
        varying vec3 vColor;

        void main()
        {
            gl_FragColor = vec4(vColor, 1.0);
        }
        """;

    private int _vao;
    private int _vbo;
    private int _shader;
    private int _uMvp;
    private int _uPointSize;
    private int _aPos;
    private int _aCol;
    private int _pointCount;
    public void Initialize(OpenGlApi gl)
    {
        _shader = BuildProgram(gl);
        _uMvp = gl.GetUniformLocation(_shader, "uMvp");
        _uPointSize = gl.GetUniformLocation(_shader, "uPointSize");
        _aPos = gl.GetAttribLocation(_shader, "aPos");
        _aCol = gl.GetAttribLocation(_shader, "aCol");
        gl.Enable(OpenGlApi.DepthTest);
        gl.Enable(OpenGlApi.ProgramPointSize);
    }

    public void Upload(OpenGlApi gl, PointData[] points, int count, float radius)
    {
        DisposeGl(gl);

        _pointCount = Math.Clamp(count, 0, points.Length);
        if (_pointCount == 0)
            return;

        _shader = BuildProgram(gl);
        _uMvp = gl.GetUniformLocation(_shader, "uMvp");
        _uPointSize = gl.GetUniformLocation(_shader, "uPointSize");
        _aPos = gl.GetAttribLocation(_shader, "aPos");
        _aCol = gl.GetAttribLocation(_shader, "aCol");

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        gl.BindBuffer(OpenGlApi.ArrayBuffer, _vbo);
        gl.BufferData(OpenGlApi.ArrayBuffer, _pointCount * Marshal.SizeOf<PointData>(), points, OpenGlApi.StaticDraw);

        gl.VertexAttribPointer(_aPos, 3, OpenGlApi.Float, false, Marshal.SizeOf<PointData>(), 0);
        gl.EnableVertexAttribArray(_aPos);
        gl.VertexAttribPointer(_aCol, 3, OpenGlApi.Float, false, Marshal.SizeOf<PointData>(), 12);
        gl.EnableVertexAttribArray(_aCol);
        gl.BindVertexArray(0);
    }

    public int Render(OpenGlApi gl, float[] mvp, float pointSize)
    {
        if (_pointCount <= 0 || _shader == 0 || _vao == 0)
            return 0;

        gl.UseProgram(_shader);
        gl.UniformMatrix4(_uMvp, mvp);
        gl.Uniform1(_uPointSize, pointSize);
        gl.BindVertexArray(_vao);
        gl.DrawArrays(OpenGlApi.Points, 0, _pointCount);
        gl.BindVertexArray(0);
        return _pointCount;
    }

    public void Dispose()
    {
    }

    public void DisposeGl(OpenGlApi gl)
    {
        if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
        if (_shader != 0) { gl.DeleteProgram(_shader); _shader = 0; }
        _pointCount = 0;
    }

    private static int BuildProgram(OpenGlApi gl)
    {
        try
        {
            return BuildProgram(gl, DesktopVertexShader, DesktopFragmentShader);
        }
        catch
        {
            return BuildProgram(gl, EsVertexShader, EsFragmentShader);
        }
    }

    private static int BuildProgram(OpenGlApi gl, string vertexSource, string fragmentSource)
    {
        int vertex = Compile(gl, OpenGlApi.VertexShader, vertexSource);
        int fragment = Compile(gl, OpenGlApi.FragmentShader, fragmentSource);
        int program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        gl.LinkProgram(program);

        if (gl.GetProgramStatus(program, OpenGlApi.LinkStatus) == 0)
            throw new InvalidOperationException(gl.GetProgramInfoLog(program));

        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        return program;
    }

    private static int Compile(OpenGlApi gl, int type, string source)
    {
        int shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        if (gl.GetShaderStatus(shader, OpenGlApi.CompileStatus) == 0)
            throw new InvalidOperationException(gl.GetShaderInfoLog(shader));

        return shader;
    }
}
