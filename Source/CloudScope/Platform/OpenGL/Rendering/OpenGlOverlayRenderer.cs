using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    public sealed class OpenGlOverlayRenderer : IOverlayRenderer
    {
        private int _lineShader = -1, _sphereShader = -1;
        private int _uViewLine, _uProjLine;
        private int _uViewSphere, _uProjSphere, _uPointSizeSphere;
        private int _uAlphaLine, _uAlphaSphere;

        private int _pivotVao = -1, _pivotVbo = -1, _pivotVertexCount;
        private int _sphereVao = -1, _sphereVbo = -1;
        private int _crosshairVao = -1, _crosshairVbo = -1;

        private readonly float[] _crossData = new float[24];
        private readonly float[] _shadowData = new float[24];
        private readonly float[] _indData = new float[24];

        private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aCol;

out vec3 vColor;

uniform mat4 view;
uniform mat4 projection;
uniform float pointSize;

void main()
{
    gl_Position  = projection * view * vec4(aPos, 1.0);
    gl_PointSize = pointSize;
    vColor = aCol;
}
";

        private const string LineFragSrc = @"
#version 330 core
in  vec3 vColor;
out vec4 FragColor;
uniform float uAlpha;

void main()
{
    FragColor = vec4(vColor, uAlpha);
}
";

        private const string SphereFragSrc = @"
#version 330 core
out vec4 FragColor;
uniform float uAlpha;

void main()
{
    vec2 p = gl_PointCoord * 2.0 - vec2(1.0);
    float r2 = dot(p, p);
    if (r2 > 1.0) discard;

    float edge = smoothstep(0.85, 1.0, sqrt(r2));

    float z = sqrt(1.0 - r2);
    vec3 normal = vec3(p.x, -p.y, z);
    vec3 lightDir = normalize(vec3(1.0, 1.5, 1.0));
    float diff = max(dot(normal, lightDir), 0.25);

    vec3 core  = vec3(1.0, 0.92, 0.2) * diff;
    vec3 glow  = vec3(1.0, 0.7, 0.0);
    vec3 color = mix(core, glow, edge);
    FragColor  = vec4(color, uAlpha);
}
";

        public void Initialize()
        {
            _lineShader = BuildShader(VertSrc, LineFragSrc);
            _uViewLine = GL.GetUniformLocation(_lineShader, "view");
            _uProjLine = GL.GetUniformLocation(_lineShader, "projection");
            _uAlphaLine = GL.GetUniformLocation(_lineShader, "uAlpha");

            _sphereShader = BuildShader(VertSrc, SphereFragSrc);
            _uViewSphere = GL.GetUniformLocation(_sphereShader, "view");
            _uProjSphere = GL.GetUniformLocation(_sphereShader, "projection");
            _uPointSizeSphere = GL.GetUniformLocation(_sphereShader, "pointSize");
            _uAlphaSphere = GL.GetUniformLocation(_sphereShader, "uAlpha");
        }

        public void RenderPivotIndicator(
            IRenderFrameData frameData,
            ref Matrix4 view,
            ref Matrix4 proj,
            OrbitCamera camera,
            Vector3 pivot,
            float fade,
            float flash)
        {
            EnsurePivotResources();

            float eff = Math.Clamp(fade + flash, 0f, 1f);
            float spherePx = 11f + 11f * fade + flash * 14f;

            float scale = camera.PivotIndicatorScaleAt(pivot);
            Matrix4 model = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(pivot);
            Matrix4 mv = model * view;

            GL.UseProgram(_lineShader);
            GL.UniformMatrix4(_uViewLine, false, ref mv);
            GL.UniformMatrix4(_uProjLine, false, ref proj);
            GL.BindVertexArray(_pivotVao);

            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Uniform1(_uAlphaLine, eff);
            GL.LineWidth(1f + eff);
            GL.DrawArrays(PrimitiveType.Lines, 0, _pivotVertexCount);

            GL.Disable(EnableCap.DepthTest);
            GL.Uniform1(_uAlphaLine, eff * 0.20f);
            GL.LineWidth(1.0f);
            GL.DrawArrays(PrimitiveType.Lines, 0, _pivotVertexCount);

            GL.DepthMask(true);
            RenderPivotSphere(ref view, ref proj, pivot, spherePx, eff);
        }

        public void RenderCenterCrosshair(IRenderFrameData frameData, int width, int height, float alpha)
        {
            EnsureCrosshairResources();

            float sX = 15f / width;
            float sY = 15f / height;
            const float g = 0.5f;

            _crossData[0] = -sX; _crossData[1] = 0f;  _crossData[2] = 0f; _crossData[3] = g; _crossData[4] = g; _crossData[5] = g;
            _crossData[6] =  sX; _crossData[7] = 0f;  _crossData[8] = 0f; _crossData[9] = g; _crossData[10] = g; _crossData[11] = g;
            _crossData[12] = 0f; _crossData[13] = -sY; _crossData[14] = 0f; _crossData[15] = g; _crossData[16] = g; _crossData[17] = g;
            _crossData[18] = 0f; _crossData[19] =  sY; _crossData[20] = 0f; _crossData[21] = g; _crossData[22] = g; _crossData[23] = g;

            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _crossData.Length * sizeof(float), _crossData, BufferUsageHint.DynamicDraw);

            GL.UseProgram(_lineShader);
            GL.Uniform1(_uAlphaLine, alpha * 0.55f);
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uProjLine, false, ref ident);

            GL.Disable(EnableCap.DepthTest);

            Matrix4 shadow = Matrix4.CreateTranslation(1f / width, -1f / height, 0);
            GL.UniformMatrix4(_uViewLine, false, ref shadow);
            FillCrosshairShadow(sX, sY);
            GL.BufferData(BufferTarget.ArrayBuffer, _shadowData.Length * sizeof(float), _shadowData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);

            GL.Uniform1(_uAlphaLine, alpha);
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.BufferData(BufferTarget.ArrayBuffer, _crossData.Length * sizeof(float), _crossData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);

            GL.Enable(EnableCap.DepthTest);
        }

        public void RenderModeIndicator(IRenderFrameData frameData, int width, int height, SelectionToolType toolType)
        {
            EnsureCrosshairResources();

            float dotX = -1f + 30f / width;
            float dotY = 1f - 30f / height;
            float r = 0f, g = 0.8f, b = 1f;

            if (toolType == SelectionToolType.Sphere)
            { r = 1f; g = 0.6f; b = 0.15f; }
            else if (toolType == SelectionToolType.Cylinder)
            { r = 0.60f; g = 0.25f; b = 1f; }

            GL.UseProgram(_lineShader);
            GL.Uniform1(_uAlphaLine, 0.9f);
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.UniformMatrix4(_uProjLine, false, ref ident);
            GL.Disable(EnableCap.DepthTest);

            float sz = 8f / width;
            float szy = 8f / height;
            _indData[0] = dotX - sz; _indData[1] = dotY;       _indData[2] = 0f; _indData[3] = r; _indData[4] = g; _indData[5] = b;
            _indData[6] = dotX + sz; _indData[7] = dotY;       _indData[8] = 0f; _indData[9] = r; _indData[10] = g; _indData[11] = b;
            _indData[12] = dotX;     _indData[13] = dotY - szy; _indData[14] = 0f; _indData[15] = r; _indData[16] = g; _indData[17] = b;
            _indData[18] = dotX;     _indData[19] = dotY + szy; _indData[20] = 0f; _indData[21] = r; _indData[22] = g; _indData[23] = b;

            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _indData.Length * sizeof(float), _indData, BufferUsageHint.DynamicDraw);
            GL.LineWidth(2.5f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);
            GL.Enable(EnableCap.DepthTest);
        }

        private void EnsurePivotResources()
        {
            if (_pivotVao != -1) return;

            const int segments = 96;
            _pivotVertexCount = 6 + 3 * segments * 2;
            float[] pivotData = new float[_pivotVertexCount * 6];
            int idx = 0;

            void AddV(float x, float y, float z, float r, float g, float b)
            {
                pivotData[idx++] = x; pivotData[idx++] = y; pivotData[idx++] = z;
                pivotData[idx++] = r; pivotData[idx++] = g; pivotData[idx++] = b;
            }

            const float ax = 0.55f;
            AddV(-ax, 0f, 0f, 1f, 0.3f, 0.3f); AddV(ax, 0f, 0f, 1f, 0.3f, 0.3f);
            AddV(0f, -ax, 0f, 0.3f, 1f, 0.3f); AddV(0f, ax, 0f, 0.3f, 1f, 0.3f);
            AddV(0f, 0f, -ax, 0.3f, 0.5f, 1f); AddV(0f, 0f, ax, 0.3f, 0.5f, 1f);

            float step = MathF.PI * 2f / segments;
            (float r, float g, float b)[] cols = {
                (1f, 0.35f, 0.35f),
                (0.35f, 1f, 0.35f),
                (0.35f, 0.6f, 1f),
            };

            for (int c = 0; c < 3; c++)
            {
                var (cr, cg, cb) = cols[c];
                for (int i = 0; i < segments; i++)
                {
                    float a1 = i * step, a2 = (i + 1) * step;
                    float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                    float c2 = MathF.Cos(a2), s2 = MathF.Sin(a2);
                    if (c == 0) { AddV(0, c1, s1, cr, cg, cb); AddV(0, c2, s2, cr, cg, cb); }
                    else if (c == 1) { AddV(c1, 0, s1, cr, cg, cb); AddV(c2, 0, s2, cr, cg, cb); }
                    else { AddV(c1, s1, 0, cr, cg, cb); AddV(c2, s2, 0, cr, cg, cb); }
                }
            }

            _pivotVao = GL.GenVertexArray();
            _pivotVbo = GL.GenBuffer();
            GL.BindVertexArray(_pivotVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _pivotVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, pivotData.Length * sizeof(float), pivotData, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, 12);
            GL.EnableVertexAttribArray(1);
        }

        private void RenderPivotSphere(ref Matrix4 view, ref Matrix4 proj, Vector3 pivot, float spherePx, float alpha)
        {
            if (_sphereVao == -1)
            {
                _sphereVao = GL.GenVertexArray();
                _sphereVbo = GL.GenBuffer();
                GL.BindVertexArray(_sphereVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
                GL.EnableVertexAttribArray(0);
            }

            float[] sphereData = { pivot.X, pivot.Y, pivot.Z };
            GL.BindVertexArray(_sphereVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 3 * sizeof(float), sphereData, BufferUsageHint.DynamicDraw);

            GL.UseProgram(_sphereShader);
            GL.UniformMatrix4(_uViewSphere, false, ref view);
            GL.UniformMatrix4(_uProjSphere, false, ref proj);
            GL.Uniform1(_uPointSizeSphere, spherePx);
            GL.Uniform1(_uAlphaSphere, alpha);
            GL.DrawArrays(PrimitiveType.Points, 0, 1);
            GL.Enable(EnableCap.DepthTest);
        }

        private void EnsureCrosshairResources()
        {
            if (_crosshairVao != -1) return;

            _crosshairVao = GL.GenVertexArray();
            _crosshairVbo = GL.GenBuffer();
            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, 12);
            GL.EnableVertexAttribArray(1);
        }

        private void FillCrosshairShadow(float sX, float sY)
        {
            _shadowData[0] = -sX; _shadowData[1] = 0f;  _shadowData[2] = 0f; _shadowData[3] = 0f; _shadowData[4] = 0f; _shadowData[5] = 0f;
            _shadowData[6] =  sX; _shadowData[7] = 0f;  _shadowData[8] = 0f; _shadowData[9] = 0f; _shadowData[10] = 0f; _shadowData[11] = 0f;
            _shadowData[12] = 0f; _shadowData[13] = -sY; _shadowData[14] = 0f; _shadowData[15] = 0f; _shadowData[16] = 0f; _shadowData[17] = 0f;
            _shadowData[18] = 0f; _shadowData[19] =  sY; _shadowData[20] = 0f; _shadowData[21] = 0f; _shadowData[22] = 0f; _shadowData[23] = 0f;
        }

        public void Dispose()
        {
            if (_pivotVao != -1) GL.DeleteVertexArray(_pivotVao);
            if (_pivotVbo != -1) GL.DeleteBuffer(_pivotVbo);
            if (_sphereVao != -1) GL.DeleteVertexArray(_sphereVao);
            if (_sphereVbo != -1) GL.DeleteBuffer(_sphereVbo);
            if (_crosshairVao != -1) GL.DeleteVertexArray(_crosshairVao);
            if (_crosshairVbo != -1) GL.DeleteBuffer(_crosshairVbo);
            if (_lineShader != -1) GL.DeleteProgram(_lineShader);
            if (_sphereShader != -1) GL.DeleteProgram(_sphereShader);
        }

        private static int BuildShader(string vertSrc, string fragSrc)
        {
            int v = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(v, vertSrc);
            GL.CompileShader(v);
            CheckShader(v, "vertex");

            int f = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(f, fragSrc);
            GL.CompileShader(f);
            CheckShader(f, "fragment");

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, v);
            GL.AttachShader(prog, f);
            GL.LinkProgram(prog);

            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
                throw new InvalidOperationException("Shader link:\n" + GL.GetProgramInfoLog(prog));

            GL.DeleteShader(v);
            GL.DeleteShader(f);
            return prog;
        }

        private static void CheckShader(int shader, string name)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
                throw new InvalidOperationException($"{name} shader:\n" + GL.GetShaderInfoLog(shader));
        }
    }
}
