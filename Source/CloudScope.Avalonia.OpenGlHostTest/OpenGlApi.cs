using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Avalonia.OpenGL;

namespace CloudScope.Avalonia.OpenGlHostTest;

internal sealed class OpenGlApi
{
    public const int Framebuffer = 0x8D40;
    public const int ColorBufferBit = 0x00004000;
    public const int DepthBufferBit = 0x00000100;
    public const int DepthTest = 0x0B71;
    public const int ProgramPointSize = 0x8642;
    public const int ArrayBuffer = 0x8892;
    public const int StaticDraw = 0x88E4;
    public const int Float = 0x1406;
    public const int False = 0;
    public const int VertexShader = 0x8B31;
    public const int FragmentShader = 0x8B30;
    public const int CompileStatus = 0x8B81;
    public const int LinkStatus = 0x8B82;
    public const int Points = 0x0000;

    private static readonly ConditionalWeakTable<GlInterface, OpenGlApi> Cache = new();

    private readonly GlBindFramebuffer _bindFramebuffer;
    private readonly GlViewport _viewport;
    private readonly GlClearColor _clearColor;
    private readonly GlClear _clear;
    private readonly GlFlush _flush;
    private readonly GlEnable _enable;
    private readonly GlGenVertexArrays _genVertexArrays;
    private readonly GlBindVertexArray _bindVertexArray;
    private readonly GlDeleteVertexArrays _deleteVertexArrays;
    private readonly GlGenBuffers _genBuffers;
    private readonly GlBindBuffer _bindBuffer;
    private readonly GlBufferData _bufferData;
    private readonly GlDeleteBuffers _deleteBuffers;
    private readonly GlVertexAttribPointer _vertexAttribPointer;
    private readonly GlEnableVertexAttribArray _enableVertexAttribArray;
    private readonly GlCreateShader _createShader;
    private readonly GlShaderSource _shaderSource;
    private readonly GlCompileShader _compileShader;
    private readonly GlGetShaderiv _getShaderiv;
    private readonly GlGetShaderInfoLog _getShaderInfoLog;
    private readonly GlDeleteShader _deleteShader;
    private readonly GlCreateProgram _createProgram;
    private readonly GlAttachShader _attachShader;
    private readonly GlLinkProgram _linkProgram;
    private readonly GlGetProgramiv _getProgramiv;
    private readonly GlGetProgramInfoLog _getProgramInfoLog;
    private readonly GlDeleteProgram _deleteProgram;
    private readonly GlUseProgram _useProgram;
    private readonly GlGetAttribLocation _getAttribLocation;
    private readonly GlGetUniformLocation _getUniformLocation;
    private readonly GlUniformMatrix4fv _uniformMatrix4fv;
    private readonly GlUniform1f _uniform1f;
    private readonly GlDrawArrays _drawArrays;

    private OpenGlApi(GlInterface gl)
    {
        _bindFramebuffer = Load<GlBindFramebuffer>(gl, "glBindFramebuffer");
        _viewport = Load<GlViewport>(gl, "glViewport");
        _clearColor = Load<GlClearColor>(gl, "glClearColor");
        _clear = Load<GlClear>(gl, "glClear");
        _flush = Load<GlFlush>(gl, "glFlush");
        _enable = Load<GlEnable>(gl, "glEnable");
        _genVertexArrays = Load<GlGenVertexArrays>(gl, "glGenVertexArrays");
        _bindVertexArray = Load<GlBindVertexArray>(gl, "glBindVertexArray");
        _deleteVertexArrays = Load<GlDeleteVertexArrays>(gl, "glDeleteVertexArrays");
        _genBuffers = Load<GlGenBuffers>(gl, "glGenBuffers");
        _bindBuffer = Load<GlBindBuffer>(gl, "glBindBuffer");
        _bufferData = Load<GlBufferData>(gl, "glBufferData");
        _deleteBuffers = Load<GlDeleteBuffers>(gl, "glDeleteBuffers");
        _vertexAttribPointer = Load<GlVertexAttribPointer>(gl, "glVertexAttribPointer");
        _enableVertexAttribArray = Load<GlEnableVertexAttribArray>(gl, "glEnableVertexAttribArray");
        _createShader = Load<GlCreateShader>(gl, "glCreateShader");
        _shaderSource = Load<GlShaderSource>(gl, "glShaderSource");
        _compileShader = Load<GlCompileShader>(gl, "glCompileShader");
        _getShaderiv = Load<GlGetShaderiv>(gl, "glGetShaderiv");
        _getShaderInfoLog = Load<GlGetShaderInfoLog>(gl, "glGetShaderInfoLog");
        _deleteShader = Load<GlDeleteShader>(gl, "glDeleteShader");
        _createProgram = Load<GlCreateProgram>(gl, "glCreateProgram");
        _attachShader = Load<GlAttachShader>(gl, "glAttachShader");
        _linkProgram = Load<GlLinkProgram>(gl, "glLinkProgram");
        _getProgramiv = Load<GlGetProgramiv>(gl, "glGetProgramiv");
        _getProgramInfoLog = Load<GlGetProgramInfoLog>(gl, "glGetProgramInfoLog");
        _deleteProgram = Load<GlDeleteProgram>(gl, "glDeleteProgram");
        _useProgram = Load<GlUseProgram>(gl, "glUseProgram");
        _getAttribLocation = Load<GlGetAttribLocation>(gl, "glGetAttribLocation");
        _getUniformLocation = Load<GlGetUniformLocation>(gl, "glGetUniformLocation");
        _uniformMatrix4fv = Load<GlUniformMatrix4fv>(gl, "glUniformMatrix4fv");
        _uniform1f = Load<GlUniform1f>(gl, "glUniform1f");
        _drawArrays = Load<GlDrawArrays>(gl, "glDrawArrays");
    }

    public static OpenGlApi Get(GlInterface gl) => Cache.GetValue(gl, static key => new OpenGlApi(key));

    public void BindFramebuffer(int target, int framebuffer) => _bindFramebuffer(target, framebuffer);

    public void Viewport(int x, int y, int width, int height) => _viewport(x, y, width, height);

    public void ClearColor(float r, float g, float b, float a) => _clearColor(r, g, b, a);

    public void Clear(int mask) => _clear(mask);

    public void Flush() => _flush();

    public void Enable(int cap) => _enable(cap);

    public int GenVertexArray()
    {
        _genVertexArrays(1, out int id);
        return id;
    }

    public void BindVertexArray(int id) => _bindVertexArray(id);

    public void DeleteVertexArray(int id) => _deleteVertexArrays(1, ref id);

    public int GenBuffer()
    {
        _genBuffers(1, out int id);
        return id;
    }

    public void BindBuffer(int target, int id) => _bindBuffer(target, id);

    public void BufferData<T>(int target, int sizeBytes, T[] data, int usage)
        where T : struct
    {
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            _bufferData(target, (nint)sizeBytes, handle.AddrOfPinnedObject(), usage);
        }
        finally
        {
            handle.Free();
        }
    }

    public void DeleteBuffer(int id) => _deleteBuffers(1, ref id);

    public void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset)
        => _vertexAttribPointer(index, size, type, normalized ? 1 : 0, stride, (nint)offset);

    public void EnableVertexAttribArray(int index) => _enableVertexAttribArray(index);

    public int CreateShader(int type) => _createShader(type);

    public void ShaderSource(int shader, string source)
    {
        string[] sources = { source };
        int[] lengths = { source.Length };
        _shaderSource(shader, 1, sources, lengths);
    }

    public void CompileShader(int shader) => _compileShader(shader);

    public int GetShaderStatus(int shader, int pname)
    {
        _getShaderiv(shader, pname, out int value);
        return value;
    }

    public string GetShaderInfoLog(int shader)
    {
        byte[] buffer = new byte[4096];
        _getShaderInfoLog(shader, buffer.Length, out int length, buffer);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Max(0, length));
    }

    public void DeleteShader(int shader) => _deleteShader(shader);

    public int CreateProgram() => _createProgram();

    public void AttachShader(int program, int shader) => _attachShader(program, shader);

    public void LinkProgram(int program) => _linkProgram(program);

    public int GetProgramStatus(int program, int pname)
    {
        _getProgramiv(program, pname, out int value);
        return value;
    }

    public string GetProgramInfoLog(int program)
    {
        byte[] buffer = new byte[4096];
        _getProgramInfoLog(program, buffer.Length, out int length, buffer);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Max(0, length));
    }

    public void DeleteProgram(int program) => _deleteProgram(program);

    public void UseProgram(int program) => _useProgram(program);

    public int GetAttribLocation(int program, string name) => _getAttribLocation(program, name);

    public int GetUniformLocation(int program, string name) => _getUniformLocation(program, name);

    public void UniformMatrix4(int location, float[] matrix) => _uniformMatrix4fv(location, 1, 0, matrix);

    public void Uniform1(int location, float value) => _uniform1f(location, value);

    public void DrawArrays(int mode, int first, int count) => _drawArrays(mode, first, count);

    private static T Load<T>(GlInterface gl, string name)
        where T : Delegate
    {
        IntPtr address = gl.GetProcAddress(name);
        if (address == IntPtr.Zero)
            throw new InvalidOperationException($"OpenGL function not available: {name}");

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindFramebuffer(int target, int framebuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlViewport(int x, int y, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlClearColor(float red, float green, float blue, float alpha);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlClear(int mask);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlFlush();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlEnable(int cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGenVertexArrays(int n, out int arrays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindVertexArray(int array);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteVertexArrays(int n, ref int arrays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGenBuffers(int n, out int buffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindBuffer(int target, int buffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBufferData(int target, nint size, IntPtr data, int usage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteBuffers(int n, ref int buffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlVertexAttribPointer(int index, int size, int type, int normalized, int stride, nint pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlEnableVertexAttribArray(int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlCreateShader(int type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlShaderSource(int shader, int count, string[] strings, int[] lengths);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlCompileShader(int shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGetShaderiv(int shader, int pname, out int parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGetShaderInfoLog(int shader, int maxLength, out int length, byte[] infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteShader(int shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlCreateProgram();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlAttachShader(int program, int shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlLinkProgram(int program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGetProgramiv(int program, int pname, out int parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGetProgramInfoLog(int program, int maxLength, out int length, byte[] infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteProgram(int program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlUseProgram(int program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlGetAttribLocation(int program, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GlGetUniformLocation(int program, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlUniformMatrix4fv(int location, int count, int transpose, float[] value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlUniform1f(int location, float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDrawArrays(int mode, int first, int count);
}
