using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Sections;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    internal sealed class OpenGlOverlayRenderer : IOverlayRenderer
    {
        private int _lineShader = -1, _sphereShader = -1;
        private int _uViewLine, _uProjLine;
        private int _uViewSphere, _uProjSphere, _uPointSizeSphere;
        private int _uAlphaLine, _uAlphaSphere;

        private int[] _pivotVbos = Array.Empty<int>();
        private PivotLineBatch[] _pivotBatches = Array.Empty<PivotLineBatch>();
        private readonly OpenGlWideLineRenderer _wideLines = new();
        private readonly OpenGlSmoothPolylineRenderer _smoothLines = new();
        private int _sphereVao = -1, _sphereVbo = -1;
        private int _crosshairVao = -1, _crosshairVbo = -1;

        private readonly float[] _crossData = new float[24];
        private readonly float[] _shadowData = new float[24];
        private readonly float[] _indData = new float[24];
        private readonly float[] _viewportBorderData = new float[30];
        private readonly float[] _sectionGuideVertices = new float[48];
        private int _sectionGuideVbo = -1;
        private float[] _gripVertices = Array.Empty<float>();
        private int _gripVbo = -1;
        private int _polylineVbo = -1;

        /// <summary>Interleaved position + color vertices, as the overlay buffers store them.</summary>
        private const int PivotVertexStride = 6 * sizeof(float);

        /// <summary>Pixel width of the selection-mode indicator cross.</summary>
        private const float ModeIndicatorWidth = 2.5f;

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
    float radius = sqrt(r2);
    float feather = max(fwidth(radius), 0.001);
    float coverage = 1.0 - smoothstep(1.0 - feather, 1.0, radius);
    if (coverage <= 0.0) discard;

    float edge = smoothstep(0.85, 1.0, radius);

    float z = sqrt(max(1.0 - r2, 0.0));
    vec3 normal = vec3(p.x, -p.y, z);
    vec3 lightDir = normalize(vec3(1.0, 1.5, 1.0));
    float diff = max(dot(normal, lightDir), 0.25);

    vec3 core  = vec3(1.0, 0.92, 0.2) * diff;
    vec3 glow  = vec3(1.0, 0.7, 0.0);
    vec3 color = mix(core, glow, edge);
    FragColor  = vec4(color, uAlpha * coverage);
}
";

        public void Initialize()
        {
            _lineShader = OpenGlShaderCompiler.CreateProgram(VertSrc, LineFragSrc, "overlay");
            _uViewLine = GL.GetUniformLocation(_lineShader, "view");
            _uProjLine = GL.GetUniformLocation(_lineShader, "projection");
            _uAlphaLine = GL.GetUniformLocation(_lineShader, "uAlpha");

            _wideLines.EnsureResources();
            _sphereShader = OpenGlShaderCompiler.CreateProgram(VertSrc, SphereFragSrc, "overlay");
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
            Matrix4 pivotMvp = mv * proj;
            float lineWidth = 1f + eff;

            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Uniform1(_uAlphaLine, eff);
            DrawPivotBatches(ref pivotMvp, eff, lineWidth);

            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Greater);
            GL.Uniform1(_uAlphaLine, eff * 0.20f);
            DrawPivotBatches(ref pivotMvp, eff * 0.20f, LineWidth.NativeMax);
            GL.DepthFunc(DepthFunction.Less);

            GL.DepthMask(true);
            RenderPivotSphere(ref view, ref proj, pivot, spherePx, eff);
        }

        private void DrawPivotBatches(ref Matrix4 mvp, float alpha, float widthPixels)
        {
            for (int i = 0; i < _pivotBatches.Length; i++)
            {
                PivotLineBatch batch = _pivotBatches[i];
                if (batch.IsClosedLoop)
                {
                    int vertexCount = (batch.PointCount + 1) * 2;
                    _smoothLines.Draw(_pivotVbos[i], 0, vertexCount, ref mvp,
                        new Vector4(batch.Color, alpha), widthPixels);
                }
                else
                {
                    _wideLines.Draw(_pivotVbos[i], 0, batch.PointCount, ref mvp,
                        new Vector4(batch.Color, alpha), widthPixels);
                }
            }

            GL.UseProgram(_lineShader);
        }

        public void RenderCenterCrosshair(IRenderFrameData frameData, int width, int height, float alpha)
        {
            EnsureCrosshairResources();

            Vector2 extent = OverlayLayout.CrosshairExtent(width, height);
            float sX = extent.X;
            float sY = extent.Y;
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

            (Vector2 center, Vector2 extent) = OverlayLayout.ModeIndicator(width, height);
            Vector3 color = OverlayLayout.ModeColor(toolType);
            float dotX = center.X;
            float dotY = center.Y;
            float r = color.X, g = color.Y, b = color.Z;

            GL.UseProgram(_lineShader);
            GL.Uniform1(_uAlphaLine, 0.9f);
            Matrix4 ident = Matrix4.Identity;
            GL.UniformMatrix4(_uViewLine, false, ref ident);
            GL.UniformMatrix4(_uProjLine, false, ref ident);
            GL.Disable(EnableCap.DepthTest);

            float sz = extent.X;
            float szy = extent.Y;
            _indData[0] = dotX - sz; _indData[1] = dotY;       _indData[2] = 0f; _indData[3] = r; _indData[4] = g; _indData[5] = b;
            _indData[6] = dotX + sz; _indData[7] = dotY;       _indData[8] = 0f; _indData[9] = r; _indData[10] = g; _indData[11] = b;
            _indData[12] = dotX;     _indData[13] = dotY - szy; _indData[14] = 0f; _indData[15] = r; _indData[16] = g; _indData[17] = b;
            _indData[18] = dotX;     _indData[19] = dotY + szy; _indData[20] = 0f; _indData[21] = r; _indData[22] = g; _indData[23] = b;

            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _indData.Length * sizeof(float), _indData, BufferUsageHint.DynamicDraw);
            Matrix4 screenSpace = Matrix4.Identity;
            _wideLines.Draw(_crosshairVbo, 0, 4, ref screenSpace, new Vector4(color, 0.9f),
                ModeIndicatorWidth, PivotVertexStride);
            GL.Enable(EnableCap.DepthTest);
        }

        public void RenderViewportBorder(IRenderFrameData frameData, int width, int height, bool active)
        {
            EnsureCrosshairResources();
            float insetX = 1f / Math.Max(width, 1);
            float insetY = 1f / Math.Max(height, 1);
            float left = -1f + insetX, right = 1f - insetX;
            float bottom = -1f + insetY, top = 1f - insetY;
            Vector3 color = active ? new Vector3(0.10f, 0.72f, 1f) : new Vector3(0.34f, 0.37f, 0.41f);
            WriteBorderVertex(0, left, top, color);
            WriteBorderVertex(1, right, top, color);
            WriteBorderVertex(2, right, bottom, color);
            WriteBorderVertex(3, left, bottom, color);
            WriteBorderVertex(4, left, top, color);

            GL.BindVertexArray(_crosshairVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _viewportBorderData.Length * sizeof(float),
                _viewportBorderData, BufferUsageHint.DynamicDraw);
            GL.UseProgram(_lineShader);
            Matrix4 identity = Matrix4.Identity;
            GL.UniformMatrix4(_uViewLine, false, ref identity);
            GL.UniformMatrix4(_uProjLine, false, ref identity);
            GL.Uniform1(_uAlphaLine, active ? 1f : 0.9f);
            GL.Disable(EnableCap.DepthTest);
            GL.LineWidth(1f);
            GL.DrawArrays(PrimitiveType.LineStrip, 0, 5);
            GL.Enable(EnableCap.DepthTest);
        }

        public void RenderSectionGuide(
            IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj, SectionDefinition section)
        {
            Vector3 normal = section.Normal;
            Vector3 along = section.Along;
            float halfWidth = section.Width * 0.5f;
            Vector3 s0 = section.Start + normal * halfWidth;
            Vector3 s1 = section.Start - normal * halfWidth;
            Vector3 e0 = section.End + normal * halfWidth;
            Vector3 e1 = section.End - normal * halfWidth;
            int vertex = 0;
            AddSectionSegment(ref vertex, s0, e0);
            AddSectionSegment(ref vertex, e0, e1);
            AddSectionSegment(ref vertex, e1, s1);
            AddSectionSegment(ref vertex, s1, s0);
            AddSectionSegment(ref vertex, section.Start, section.End);

            float arrowLength = MathF.Max(section.Width, section.Length * 0.08f);
            Vector3 tip = section.Center + normal * arrowLength;
            AddSectionSegment(ref vertex, section.Center, tip);
            AddSectionSegment(ref vertex, tip, tip - normal * arrowLength * 0.32f + along * arrowLength * 0.22f);
            AddSectionSegment(ref vertex, tip, tip - normal * arrowLength * 0.32f - along * arrowLength * 0.22f);

            if (_sectionGuideVbo == -1)
                _sectionGuideVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _sectionGuideVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _sectionGuideVertices.Length * sizeof(float),
                _sectionGuideVertices, BufferUsageHint.DynamicDraw);
            Matrix4 mvp = view * proj;
            GL.Disable(EnableCap.DepthTest);
            _wideLines.Draw(_sectionGuideVbo, 0, vertex, ref mvp,
                new Vector4(1f, 0.72f, 0.12f, 0.95f), 2f);
            GL.Enable(EnableCap.DepthTest);
        }

        public void RenderGrips(
            IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj,
            OrbitCamera camera, IReadOnlyList<GripDescriptor> grips, int hovered, int active)
        {
            if (grips.Count == 0) return;
            int required = grips.Count * GripOverlayGeometry.FloatsPerGrip;
            if (_gripVertices.Length < required) _gripVertices = new float[required];
            int count = GripOverlayGeometry.Fill(grips, camera, _gripVertices);
            if (_gripVbo == -1) _gripVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _gripVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, required * sizeof(float), _gripVertices, BufferUsageHint.DynamicDraw);

            Matrix4 mvp = view * proj;
            GL.Disable(EnableCap.DepthTest);
            for (int i = 0; i < count; i++)
                _wideLines.Draw(_gripVbo, i * GripOverlayGeometry.VerticesPerGrip,
                    GripOverlayGeometry.VerticesPerGrip, ref mvp,
                    GripOverlayGeometry.Color(grips[i].Index, hovered, active), 2f);
            GL.Enable(EnableCap.DepthTest);
        }

        public void RenderPolyline(
            IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj,
            IReadOnlyList<Vector3> points, bool closed, Vector4 color, float widthPixels, bool depthTest = true)
        {
            // A connected triangle strip can turn inside-out at a projected 3D bend.
            // Independent screen-space capsules retain their pixel width under rotation.
            float[] vertices = PolylineRenderGeometry.BuildSegments(points, closed);
            if (vertices.Length == 0) return;
            if (_polylineVbo == -1) _polylineVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _polylineVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            Matrix4 mvp = view * proj;
            if (depthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            // Match Metal and the pivot renderer: test the line against the scene, but never
            // let its antialiased capsule fragments write depth and mask adjacent segments.
            GL.DepthMask(false);
            _wideLines.Draw(_polylineVbo, 0, vertices.Length / 3, ref mvp, color, widthPixels);
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        public void RenderSnapIndicator(
            IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj,
            OrbitCamera camera, ObjectSnapResult snap)
        {
            if (!snap.IsSnapped) return;
            if (snap.GuideStart is { } start && snap.GuideEnd is { } end)
                RenderPolyline(frameData, ref view, ref proj, [start, end], false,
                    GripOverlayGeometry.SnapColor(snap.Kind, 0.72f), 1.25f, depthTest: false);

            GripKind kind = snap.Kind == ObjectSnapKind.Midpoint ? GripKind.Midpoint : GripKind.Endpoint;
            GripDescriptor[] marker = [new(0, kind, snap.Position, Vector3.Zero, GripConstraint.ViewPlane)];
            float[] vertices = new float[GripOverlayGeometry.FloatsPerGrip];
            GripOverlayGeometry.Fill(marker, camera, vertices);
            if (_gripVbo == -1) _gripVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _gripVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            Matrix4 mvp = view * proj;
            GL.Disable(EnableCap.DepthTest);
            _wideLines.Draw(_gripVbo, 0, GripOverlayGeometry.VerticesPerGrip, ref mvp,
                GripOverlayGeometry.SnapColor(snap.Kind), 2f);
            GL.Enable(EnableCap.DepthTest);
        }

        private void AddSectionSegment(ref int vertex, Vector3 start, Vector3 end)
        {
            WriteSectionVertex(vertex++, start);
            WriteSectionVertex(vertex++, end);
        }

        private void WriteSectionVertex(int vertex, Vector3 point)
        {
            int i = vertex * 3;
            _sectionGuideVertices[i] = point.X;
            _sectionGuideVertices[i + 1] = point.Y;
            _sectionGuideVertices[i + 2] = point.Z;
        }

        private void WriteBorderVertex(int vertex, float x, float y, Vector3 color)
        {
            int i = vertex * 6;
            _viewportBorderData[i] = x;
            _viewportBorderData[i + 1] = y;
            _viewportBorderData[i + 2] = 0f;
            _viewportBorderData[i + 3] = color.X;
            _viewportBorderData[i + 4] = color.Y;
            _viewportBorderData[i + 5] = color.Z;
        }

        private void EnsurePivotResources()
        {
            if (_pivotVbos.Length > 0) return;

            _pivotBatches = PivotIndicatorGeometry.BuildBatches();
            _pivotVbos = new int[_pivotBatches.Length];
            for (int i = 0; i < _pivotBatches.Length; i++)
            {
                PivotLineBatch batch = _pivotBatches[i];
                float[] vertices = batch.IsClosedLoop
                    ? PivotIndicatorGeometry.BuildSmoothLoopVertices(batch.Positions)
                    : batch.Positions;
                _pivotVbos[i] = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, _pivotVbos[i]);
                GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            }
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
            _wideLines.Dispose();
            if (_sectionGuideVbo != -1)
            {
                GL.DeleteBuffer(_sectionGuideVbo);
                _sectionGuideVbo = -1;
            }
            if (_gripVbo != -1)
            {
                GL.DeleteBuffer(_gripVbo);
                _gripVbo = -1;
            }
            if (_polylineVbo != -1)
            {
                GL.DeleteBuffer(_polylineVbo);
                _polylineVbo = -1;
            }
            _smoothLines.Dispose();
            foreach (int pivotVbo in _pivotVbos)
                if (pivotVbo != -1) GL.DeleteBuffer(pivotVbo);
            if (_sphereVao != -1) GL.DeleteVertexArray(_sphereVao);
            if (_sphereVbo != -1) GL.DeleteBuffer(_sphereVbo);
            if (_crosshairVao != -1) GL.DeleteVertexArray(_crosshairVao);
            if (_crosshairVbo != -1) GL.DeleteBuffer(_crosshairVbo);
            if (_lineShader != -1) GL.DeleteProgram(_lineShader);
            if (_sphereShader != -1) GL.DeleteProgram(_sphereShader);
        }

    }
}
