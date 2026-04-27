using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTK.Mathematics;

namespace CloudScope
{
    public enum SelectionTool { None, Rect, Sphere }

    /// <summary>
    /// Manages interactive rectangle and sphere selection.
    ///
    /// Keybindings (set by PointCloudViewer):
    ///   T   Rectangle selection
    ///   G   Sphere selection
    ///   Esc Deactivate tool (if active) / quit (if None)
    ///   Del Clear current selection
    ///
    /// Mouse routing when tool is active:
    ///   Left drag   → draw / edit selection shape
    ///   Right drag  → pan  (unchanged, handled by camera)
    ///   Middle drag → pan  (unchanged)
    ///   Scroll      → zoom (unchanged)
    ///
    /// The selection shape stays active after mouse-up (re-edit mode).
    /// Handles are screen-space squares; hit radius is 9 px.
    /// </summary>
    public sealed class SelectionManager
    {
        // ── Viewport ──────────────────────────────────────────────────────────
        private int _vpW = 1600, _vpH = 900;

        // ── Tool / phase state ────────────────────────────────────────────────
        public SelectionTool ActiveTool { get; private set; } = SelectionTool.None;

        private enum Phase { Idle, Drawing, Editing }
        private Phase _phase = Phase.Idle;

        // ── Rect (pixel coords, not necessarily normalized during draw) ────────
        // After each commit _rx0<=_rx1, _ry0<=_ry1 is guaranteed.
        private float _rx0, _ry0, _rx1, _ry1;
        private int   _activeHandle = -2; // -2=none, -1=move, 0-7=corner/edge
        private float _moveDX, _moveDY;   // cursor offset from rect TL on move-drag start

        // ── Sphere ────────────────────────────────────────────────────────────
        private Vector3 _sCenter;
        private float   _sRadius;
        private bool    _sDragRadius;     // true=drag radius handle, false=drag center

        // ── Selection results ─────────────────────────────────────────────────
        public int   SelectedCount { get; private set; }
        public bool  HasSelection  => _phase == Phase.Editing && SelectedCount >= 0;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action? SelectionChanged;

        // ── Rendering vertex buffer ────────────────────────────────────────────
        private readonly List<float> _verts = new(4096);

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public void SetViewportSize(int w, int h) { _vpW = w; _vpH = h; }

        public void SetTool(SelectionTool tool)
        {
            ActiveTool    = tool;
            _phase        = Phase.Idle;
            SelectedCount = 0;
            SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            _phase        = Phase.Idle;
            SelectedCount = 0;
            SelectionChanged?.Invoke();
        }

        // ── Mouse events — only called for the LEFT button ────────────────────

        /// <summary>Returns true if the left-button down was consumed by the selection tool.</summary>
        public bool OnLeftDown(int mx, int my, OrbitCamera cam)
        {
            if (ActiveTool == SelectionTool.None) return false;
            return ActiveTool == SelectionTool.Rect ? RectDown(mx, my) : SphereDown(mx, my, cam);
        }

        /// <summary>Returns true if mouse move was consumed by the selection tool.</summary>
        public bool OnLeftMove(int mx, int my, OrbitCamera cam)
        {
            if (ActiveTool == SelectionTool.None || _phase == Phase.Idle) return false;
            return ActiveTool == SelectionTool.Rect ? RectMove(mx, my) : SphereMove(mx, my, cam);
        }

        /// <summary>
        /// Returns true if the left-button up was consumed.
        /// Triggers point selection commit when pts != null.
        /// </summary>
        public bool OnLeftUp(int mx, int my, PointData[]? pts, OrbitCamera cam)
        {
            if (ActiveTool == SelectionTool.None || _phase == Phase.Idle) return false;

            _activeHandle = -2;
            _sDragRadius  = false;

            if (_phase == Phase.Drawing)
            {
                NormalizeRect();
                _phase = Phase.Editing;
            }

            if (pts != null)
                CommitSelection(pts, cam);

            SelectionChanged?.Invoke();
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Rect logic
        // ─────────────────────────────────────────────────────────────────────

        private bool RectDown(int mx, int my)
        {
            if (_phase == Phase.Editing)
            {
                int h = HitHandle(mx, my);
                if (h >= 0)
                {
                    _activeHandle = h;
                    _phase = Phase.Drawing;
                    return true;
                }
                if (mx >= _rx0 && mx <= _rx1 && my >= _ry0 && my <= _ry1)
                {
                    _activeHandle = -1;
                    _moveDX = mx - _rx0;
                    _moveDY = my - _ry0;
                    _phase = Phase.Drawing;
                    return true;
                }
                // Clicked outside — start fresh rect
            }

            _rx0 = _rx1 = mx;
            _ry0 = _ry1 = my;
            _activeHandle = 7; // BR corner follows the mouse
            _phase = Phase.Drawing;
            return true;
        }

        private bool RectMove(int mx, int my)
        {
            if (_phase != Phase.Drawing) return false;

            if (_activeHandle == -1)
            {
                float w = _rx1 - _rx0, h = _ry1 - _ry0;
                _rx0 = mx - _moveDX;
                _ry0 = my - _moveDY;
                _rx1 = _rx0 + w;
                _ry1 = _ry0 + h;
            }
            else if (_activeHandle >= 0)
            {
                ApplyHandleDrag(_activeHandle, mx, my);
            }
            return true;
        }

        // Handles: 0=TL 1=TC 2=TR  3=ML 4=MR  5=BL 6=BC 7=BR
        private void ApplyHandleDrag(int h, int mx, int my)
        {
            switch (h)
            {
                case 0: _rx0 = mx; _ry0 = my; break;
                case 1:            _ry0 = my; break;
                case 2: _rx1 = mx; _ry0 = my; break;
                case 3: _rx0 = mx;             break;
                case 4: _rx1 = mx;             break;
                case 5: _rx0 = mx; _ry1 = my; break;
                case 6:            _ry1 = my; break;
                case 7: _rx1 = mx; _ry1 = my; break;
            }
        }

        private void NormalizeRect()
        {
            if (_rx0 > _rx1) (_rx0, _rx1) = (_rx1, _rx0);
            if (_ry0 > _ry1) (_ry0, _ry1) = (_ry1, _ry0);
        }

        // 8 handle positions (same order as ApplyHandleDrag)
        private Vector2[] GetHandlePositions()
        {
            float mx = (_rx0 + _rx1) * 0.5f;
            float my = (_ry0 + _ry1) * 0.5f;
            return new[]
            {
                new Vector2(_rx0, _ry0), new Vector2(mx,  _ry0), new Vector2(_rx1, _ry0),
                new Vector2(_rx0, my),                            new Vector2(_rx1, my),
                new Vector2(_rx0, _ry1), new Vector2(mx,  _ry1), new Vector2(_rx1, _ry1),
            };
        }

        private int HitHandle(int mx, int my)
        {
            const float hitR2 = 9f * 9f;
            var handles = GetHandlePositions();
            for (int i = 0; i < handles.Length; i++)
            {
                float dx = mx - handles[i].X, dy = my - handles[i].Y;
                if (dx * dx + dy * dy <= hitR2) return i;
            }
            return -1;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Sphere logic
        // ─────────────────────────────────────────────────────────────────────

        private bool SphereDown(int mx, int my, OrbitCamera cam)
        {
            if (_phase == Phase.Editing)
            {
                if (cam.ProjectWorldToScreen(_sCenter, out float cx, out float cy))
                {
                    float screenR = SphereScreenRadius(cam);

                    // Hit-test radius handle (rightmost point on circle)
                    float rdx = mx - (cx + screenR), rdy = my - cy;
                    if (rdx * rdx + rdy * rdy <= 81f)
                    {
                        _sDragRadius = true;
                        _phase = Phase.Drawing;
                        return true;
                    }

                    // Hit-test center handle
                    float cdx = mx - cx, cdy = my - cy;
                    if (cdx * cdx + cdy * cdy <= 81f)
                    {
                        _sDragRadius = false;
                        _phase = Phase.Drawing;
                        return true;
                    }
                }
                // Clicked outside — start fresh sphere
            }

            cam.TryPickWorldPoint(mx, my, 11, out _sCenter);
            _sRadius     = 0f;
            _sDragRadius = true;
            _phase       = Phase.Drawing;
            return true;
        }

        private bool SphereMove(int mx, int my, OrbitCamera cam)
        {
            if (_phase != Phase.Drawing) return false;

            if (_sDragRadius)
            {
                _sRadius = MathF.Max(cam.ScreenToWorldRadius(_sCenter, mx, my), 0.001f);
            }
            else
            {
                // Move center: re-pick cloud surface under cursor
                cam.TryPickWorldPoint(mx, my, 5, out _sCenter);
            }
            return true;
        }

        private float SphereScreenRadius(OrbitCamera cam)
        {
            // Project center and a world point offset by _sRadius along camera-right
            // to get the screen-space radius in pixels.
            if (!cam.ProjectWorldToScreen(_sCenter, out float cx, out float cy)) return 0f;
            Vector3 edgePt = _sCenter + cam.CameraRight * _sRadius;
            if (!cam.ProjectWorldToScreen(edgePt, out float ex, out float ey)) return 0f;
            return MathF.Sqrt((ex - cx) * (ex - cx) + (ey - cy) * (ey - cy));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Selection commit
        // ─────────────────────────────────────────────────────────────────────

        private void CommitSelection(PointData[] pts, OrbitCamera cam)
        {
            if (ActiveTool == SelectionTool.Rect)
                CommitRect(pts, cam);
            else
                CommitSphere(pts);
        }

        private void CommitRect(PointData[] pts, OrbitCamera cam)
        {
            float x0 = MathF.Min(_rx0, _rx1), x1 = MathF.Max(_rx0, _rx1);
            float y0 = MathF.Min(_ry0, _ry1), y1 = MathF.Max(_ry0, _ry1);

            if (x1 - x0 < 2f || y1 - y0 < 2f) { SelectedCount = 0; return; }

            Matrix4 view = cam.GetViewMatrix();
            Matrix4 proj = cam.GetProjectionMatrix();

            // Precompute MVP rows for inline transform
            // result = pos * view * proj (row-vector convention)
            // To avoid rebuilding matrices per-point, cache the 3 relevant rows of MVP
            // clip.x = pos*MVP_col0, clip.y = pos*MVP_col1, clip.w = pos*MVP_col3
            float vpW = _vpW, vpH = _vpH;

            // Compute MVP = view * proj in row-major OpenTK convention
            // result.Mij = sum_k view.Mik * proj.Mkj
            float m11 = view.M11 * proj.M11 + view.M12 * proj.M21 + view.M13 * proj.M31 + view.M14 * proj.M41;
            float m12 = view.M11 * proj.M12 + view.M12 * proj.M22 + view.M13 * proj.M32 + view.M14 * proj.M42;
            float m14 = view.M11 * proj.M14 + view.M12 * proj.M24 + view.M13 * proj.M34 + view.M14 * proj.M44;
            float m21 = view.M21 * proj.M11 + view.M22 * proj.M21 + view.M23 * proj.M31 + view.M24 * proj.M41;
            float m22 = view.M21 * proj.M12 + view.M22 * proj.M22 + view.M23 * proj.M32 + view.M24 * proj.M42;
            float m24 = view.M21 * proj.M14 + view.M22 * proj.M24 + view.M23 * proj.M34 + view.M24 * proj.M44;
            float m31 = view.M31 * proj.M11 + view.M32 * proj.M21 + view.M33 * proj.M31 + view.M34 * proj.M41;
            float m32 = view.M31 * proj.M12 + view.M32 * proj.M22 + view.M33 * proj.M32 + view.M34 * proj.M42;
            float m34 = view.M31 * proj.M14 + view.M32 * proj.M24 + view.M33 * proj.M34 + view.M34 * proj.M44;
            float m41 = view.M41 * proj.M11 + view.M42 * proj.M21 + view.M43 * proj.M31 + view.M44 * proj.M41;
            float m42 = view.M41 * proj.M12 + view.M42 * proj.M22 + view.M43 * proj.M32 + view.M44 * proj.M42;
            float m44 = view.M41 * proj.M14 + view.M42 * proj.M24 + view.M43 * proj.M34 + view.M44 * proj.M44;

            // Parallel count pass (thread-safe via interlocked or chunk reduction)
            int count = 0;
            object lockObj = new();

            Parallel.ForEach(
                Partitioner.Create(0, pts.Length),
                () => 0,
                (range, _, localCount) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        float px = pts[i].X, py = pts[i].Y, pz = pts[i].Z;
                        float cx = px * m11 + py * m21 + pz * m31 + m41;
                        float cy = px * m12 + py * m22 + pz * m32 + m42;
                        float cw = px * m14 + py * m24 + pz * m34 + m44;
                        if (cw <= 0f) continue;
                        float sx = (cx / cw + 1f) * 0.5f * vpW;
                        float sy = (1f - cy / cw) * 0.5f * vpH;
                        if (sx >= x0 && sx <= x1 && sy >= y0 && sy <= y1)
                            localCount++;
                    }
                    return localCount;
                },
                localCount => { lock (lockObj) count += localCount; }
            );

            SelectedCount = count;
        }

        private void CommitSphere(PointData[] pts)
        {
            if (_sRadius <= 0f) { SelectedCount = 0; return; }

            float r2  = _sRadius * _sRadius;
            float scx = _sCenter.X, scy = _sCenter.Y, scz = _sCenter.Z;
            int count = 0;
            object lockObj = new();

            Parallel.ForEach(
                Partitioner.Create(0, pts.Length),
                () => 0,
                (range, _, localCount) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        float dx = pts[i].X - scx;
                        float dy = pts[i].Y - scy;
                        float dz = pts[i].Z - scz;
                        if (dx * dx + dy * dy + dz * dz <= r2)
                            localCount++;
                    }
                    return localCount;
                },
                localCount => { lock (lockObj) count += localCount; }
            );

            SelectedCount = count;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Overlay rendering (NDC, identity view/proj in line shader)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills <paramref name="verts"/> with line-segment vertices (format: x y z r g b)
        /// in NDC space for the active selection shape.  Returns the vertex count.
        /// </summary>
        public int BuildOverlayVerts(float[] verts, OrbitCamera cam)
        {
            _verts.Clear();

            if (ActiveTool == SelectionTool.Rect && _phase != Phase.Idle)
                BuildRectOverlay();

            if (ActiveTool == SelectionTool.Sphere && _phase != Phase.Idle)
                BuildSphereOverlay(cam);

            int n = _verts.Count;
            if (verts.Length < n)
                return n; // caller needs to resize
            _verts.CopyTo(verts);
            return n;
        }

        private float PxToNdcX(float px) => px / _vpW * 2f - 1f;
        private float PxToNdcY(float py) => 1f - py / _vpH * 2f;

        private void AddLine(float x0, float y0, float x1, float y1, float r, float g, float b)
        {
            _verts.Add(x0); _verts.Add(y0); _verts.Add(0f); _verts.Add(r); _verts.Add(g); _verts.Add(b);
            _verts.Add(x1); _verts.Add(y1); _verts.Add(0f); _verts.Add(r); _verts.Add(g); _verts.Add(b);
        }

        private void AddLinePx(float px0, float py0, float px1, float py1, float r, float g, float b) =>
            AddLine(PxToNdcX(px0), PxToNdcY(py0), PxToNdcX(px1), PxToNdcY(py1), r, g, b);

        private void AddHandlePx(float cx, float cy, float r, float g, float b)
        {
            // Small square handle in NDC space
            const float hs = 4.5f; // half-size in pixels
            float hsx = hs / _vpW * 2f;
            float hsy = hs / _vpH * 2f;
            float nx = PxToNdcX(cx), ny = PxToNdcY(cy);
            AddLine(nx - hsx, ny - hsy, nx + hsx, ny - hsy, r, g, b);
            AddLine(nx + hsx, ny - hsy, nx + hsx, ny + hsy, r, g, b);
            AddLine(nx + hsx, ny + hsy, nx - hsx, ny + hsy, r, g, b);
            AddLine(nx - hsx, ny + hsy, nx - hsx, ny - hsy, r, g, b);
        }

        private void BuildRectOverlay()
        {
            float x0 = MathF.Min(_rx0, _rx1), y0 = MathF.Min(_ry0, _ry1);
            float x1 = MathF.Max(_rx0, _rx1), y1 = MathF.Max(_ry0, _ry1);

            // Selection color: cyan outline
            float cr = 0.2f, cg = 0.9f, cb = 1.0f;

            // Rect outline (4 sides)
            AddLinePx(x0, y0, x1, y0, cr, cg, cb);
            AddLinePx(x1, y0, x1, y1, cr, cg, cb);
            AddLinePx(x1, y1, x0, y1, cr, cg, cb);
            AddLinePx(x0, y1, x0, y0, cr, cg, cb);

            if (_phase != Phase.Editing) return;

            // Handles — white squares
            float hr = 1f, hg = 1f, hb = 1f;
            var handles = GetHandlePositions();
            foreach (var h in handles)
                AddHandlePx(h.X, h.Y, hr, hg, hb);
        }

        private void BuildSphereOverlay(OrbitCamera cam)
        {
            if (!cam.ProjectWorldToScreen(_sCenter, out float cx, out float cy)) return;
            float screenR = SphereScreenRadius(cam);

            if (screenR < 1f && _phase == Phase.Drawing) return;

            // Circle (64 segments) in screen-pixel space → NDC
            float cr = 1.0f, cg = 0.65f, cb = 0.1f; // orange circle
            const int seg = 64;
            for (int i = 0; i < seg; i++)
            {
                float a1 = i       / (float)seg * MathF.PI * 2f;
                float a2 = (i + 1) / (float)seg * MathF.PI * 2f;
                float px1 = cx + MathF.Cos(a1) * screenR;
                float py1 = cy + MathF.Sin(a1) * screenR;
                float px2 = cx + MathF.Cos(a2) * screenR;
                float py2 = cy + MathF.Sin(a2) * screenR;
                AddLinePx(px1, py1, px2, py2, cr, cg, cb);
            }

            if (_phase != Phase.Editing) return;

            // Center handle (white square)
            AddHandlePx(cx, cy, 1f, 1f, 1f);

            // Radius handle (white square at rightmost circle point)
            AddHandlePx(cx + screenR, cy, 1f, 1f, 1f);
        }
    }
}
