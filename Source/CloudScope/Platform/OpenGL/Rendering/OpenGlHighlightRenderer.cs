using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CloudScope.Labeling;
using CloudScope.Rendering;

namespace CloudScope.Platform.OpenGL.Rendering
{
    /// <summary>
    /// Renders labeled points as a second pass over the point cloud
    /// with distinct label colors and a slightly larger point size.
    /// Rebuilds its GPU buffer whenever <see cref="LabelManager.LabelsChanged"/> fires.
    /// </summary>
    public sealed class OpenGlHighlightRenderer : IHighlightRenderer
    {
        private int _vao = -1, _vbo = -1;
        private int _pvao = -1, _pvbo = -1;  // preview buffer (points inside active box)
        private int _shader = -1;
        private int _uView, _uProj, _uPointSize;
        private int _highlightCount;
        private int _previewCount;
        private bool _dirty = true;
        private float[] _vertexScratch = Array.Empty<float>();

        // ── Label → Color palette ─────────────────────────────────────────────
        private static readonly Dictionary<string, Vector3> Palette = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ground"]     = new Vector3(0.55f, 0.27f, 0.07f),  // brown
            ["Building"]   = new Vector3(1.0f,  0.27f, 0.27f),  // red
            ["Vegetation"] = new Vector3(0.13f, 0.80f, 0.13f),  // green
            ["Vehicle"]    = new Vector3(1.0f,  0.84f, 0.0f),   // gold
            ["Road"]       = new Vector3(0.60f, 0.60f, 0.60f),  // gray
            ["Water"]      = new Vector3(0.12f, 0.56f, 1.0f),   // blue
            ["Wire"]       = new Vector3(0.93f, 0.51f, 0.93f),  // violet
        };

        // Fallback: generate a color from the label's hash
        private static Vector3 GetLabelColor(string label)
        {
            if (Palette.TryGetValue(label, out var c)) return c;
            int h = label.GetHashCode();
            float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
            // HSV → RGB (S=0.75, V=0.9)
            float s = 0.75f, v = 0.9f;
            int hi = (int)(hue * 6f) % 6;
            float f = hue * 6f - hi;
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            return hi switch
            {
                0 => new Vector3(v, t, p),
                1 => new Vector3(q, v, p),
                2 => new Vector3(p, v, t),
                3 => new Vector3(p, q, v),
                4 => new Vector3(t, p, v),
                _ => new Vector3(v, p, q),
            };
        }

        // ── Shader (same as main cloud, but we supply the color per vertex) ──

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
        private const string FragSrc = @"
#version 330 core
in  vec3 vColor;
out vec4 FragColor;
void main()
{
    vec2  d = gl_PointCoord - vec2(0.5);
    if (dot(d, d) > 0.25) discard;
    FragColor = vec4(vColor, 1.0);
}
";

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Mark the labeled-highlight GPU buffer as needing a rebuild.</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>
        /// Upload the current box-selection preview: points inside the box shown in yellow.
        /// Call once per update tick (throttled externally). Pass null/empty to clear.
        /// </summary>
        public void UpdatePreview(PointData[]? points, IReadOnlyList<int>? indices)
        {
            EnsureResources();
            if (points == null || indices == null || indices.Count == 0) { _previewCount = 0; return; }

            float[] data = RentVertexScratch(indices.Count);
            int idx = 0;
            foreach (int i in indices)
            {
                if (i < 0 || i >= points.Length) continue;
                ref readonly PointData pt = ref points[i];
                data[idx++] = pt.X; data[idx++] = pt.Y; data[idx++] = pt.Z;
                data[idx++] = 1.0f; data[idx++] = 0.85f; data[idx++] = 0.1f; // bright yellow
            }
            _previewCount = idx / 6;
            GL.BindVertexArray(_pvao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _pvbo);
            GL.BufferData(BufferTarget.ArrayBuffer, idx * sizeof(float), data, BufferUsageHint.DynamicDraw);
        }

        /// <summary>Render the box-selection preview highlight pass.</summary>
        public void RenderPreview(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            if (_previewCount == 0 || _shader == -1) return;
            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProj, false, ref proj);
            GL.Uniform1(_uPointSize, pointSize + 2f);
            GL.BindVertexArray(_pvao);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.PolygonOffsetPoint);
            GL.PolygonOffset(-1f, -1f);
            GL.DrawArrays(PrimitiveType.Points, 0, _previewCount);
            GL.Disable(EnableCap.PolygonOffsetPoint);
        }

        /// <summary>
        /// Rebuild the highlight buffer from the current label state, then render.
        /// </summary>
        public void Render(IRenderFrameData frameData, PointData[] points, LabelManager labels,
                           ref Matrix4 view, ref Matrix4 proj, float pointSize)
        {
            if (labels.Count == 0 && !_dirty) return;

            EnsureResources();

            if (_dirty)
            {
                RebuildBuffer(points, labels);
                _dirty = false;
            }

            if (_highlightCount == 0) return;

            GL.UseProgram(_shader);
            GL.UniformMatrix4(_uView, false, ref view);
            GL.UniformMatrix4(_uProj, false, ref proj);
            GL.Uniform1(_uPointSize, pointSize + 2f);  // slightly larger to stand out

            GL.BindVertexArray(_vao);

            // Render on top with a slight depth bias so highlight wins ties
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.Enable(EnableCap.PolygonOffsetPoint);
            GL.PolygonOffset(-1f, -1f);

            GL.DrawArrays(PrimitiveType.Points, 0, _highlightCount);

            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.Disable(EnableCap.PolygonOffsetPoint);
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private void RebuildBuffer(PointData[] points, LabelManager labels)
        {
            var allLabels = labels.AllLabels;
            _highlightCount = allLabels.Count;

            if (_highlightCount == 0) return;

            // 6 floats per vertex: X Y Z R G B
            float[] data = RentVertexScratch(_highlightCount);
            int idx = 0;

            foreach (var (ptIdx, labelName) in allLabels)
            {
                if (ptIdx < 0 || ptIdx >= points.Length) continue;
                ref readonly PointData pt = ref points[ptIdx];
                var col = GetLabelColor(labelName);
                data[idx++] = pt.X; data[idx++] = pt.Y; data[idx++] = pt.Z;
                data[idx++] = col.X; data[idx++] = col.Y; data[idx++] = col.Z;
            }

            _highlightCount = idx / 6;  // actual valid count

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, idx * sizeof(float),
                          data, BufferUsageHint.DynamicDraw);
        }

        private float[] RentVertexScratch(int vertexCount)
        {
            int required = vertexCount * 6;
            if (_vertexScratch.Length < required)
                _vertexScratch = new float[required];
            return _vertexScratch;
        }

        private void EnsureResources()
        {
            if (_shader != -1) return;

            int v = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(v, VertSrc); GL.CompileShader(v);
            int f = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(f, FragSrc); GL.CompileShader(f);
            _shader = GL.CreateProgram();
            GL.AttachShader(_shader, v); GL.AttachShader(_shader, f);
            GL.LinkProgram(_shader);
            GL.DeleteShader(v); GL.DeleteShader(f);

            _uView      = GL.GetUniformLocation(_shader, "view");
            _uProj      = GL.GetUniformLocation(_shader, "projection");
            _uPointSize = GL.GetUniformLocation(_shader, "pointSize");

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, 12);
            GL.EnableVertexAttribArray(1);

            // Preview buffer (same layout)
            _pvao = GL.GenVertexArray();
            _pvbo = GL.GenBuffer();
            GL.BindVertexArray(_pvao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _pvbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, 12);
            GL.EnableVertexAttribArray(1);
        }

        public void Dispose()
        {
            if (_shader != -1) GL.DeleteProgram(_shader);
            if (_vao != -1) GL.DeleteVertexArray(_vao);
            if (_vbo != -1) GL.DeleteBuffer(_vbo);
            if (_pvao != -1) GL.DeleteVertexArray(_pvao);
            if (_pvbo != -1) GL.DeleteBuffer(_pvbo);
        }
    }
}
