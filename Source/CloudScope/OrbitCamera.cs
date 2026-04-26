using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace CloudScope
{
    /// <summary>
    /// Accurate port: AdvancedZPR/Viewport.cs -> modern OpenGL (OpenTK 4.x GameWindow).
    ///
    /// All Hilton formulas (calcViewVolume, ScreenToView, PickDepth, Pan, Zoom, Rotate)
    /// ported 1:1. Only WinForms/fixed-function calls were replaced with modern GL uniforms.
    ///
    /// Mouse controls (= AdvancedZPR):
    ///   MouseDown (any button)  -> PickDepth
    ///   Left drag               -> Rotate(dx, dy)
    ///   Right drag              -> Pan(lastPos -> currentPos)
    ///   MouseWheel              -> PickDepth, then Zoom
    ///   Space                   -> PickDepth, then ToggleProjection
    /// </summary>
    public sealed class OrbitCamera
    {
        // ── Hilton 7 view-volume parameters ───────────────────────────────────
        private float hw, hh;      // half view widths (aspect-corrected)
        private float zn, zf;      // near / far in Hilton view-space
        private float iez;         // 1/eyeZ  (0 = orthographic)
        private float tsx, tsy;    // view-translation (always 0)

        // ── View-to-World transformation (Hilton) ─────────────────────────────
        private Matrix4 _vtw = Matrix4.Identity;   // view->world rotation
        private Vector3 _trn = Vector3.Zero;        // viewToWorldTrn
        private Vector3 _orbitPivot = Vector3.Zero; // world-space rotation center

        // ── Euler angles ──────────────────────────────────────────────────────
        private float _az = 30f;
        private float _el = 25f;

        // ── Zoom / projection ─────────────────────────────────────────────────
        private double _hvs  = 50.0;                          // halfViewSize
        private double _vang = 45.0 * Math.PI / 180.0;        // 0 = ortho

        // ── Fixed scene depth range (does not change with zoom) ───────────────
        private float _sceneRadius = 50f;

        // ── Viewport size ─────────────────────────────────────────────────────
        private int _vpW = 1600, _vpH = 900;

        // ── Pick state (PickDepth writes, Pan/Zoom reads) ─────────────────────
        private Vector3 _picked = Vector3.Zero;
        private float   _pickedDepth = 1f;

        // ── Cached depth read buffer (avoids per-event allocation) ────────────
        private float[] _depthWindow = new float[1024]; // 32x32 max

        // ── Configurable parameters ───────────────────────────────────────────
        public float RotationSpeed  { get; set; } = 0.5f;
        public bool  ConstrainElev  { get; set; } = true;

        /// <summary>Returns the world coordinates of the current orbit center.</summary>
        public Vector3 Pivot => _orbitPivot;

        /// <summary>The current "half screen size" at the pivot distance.</summary>
        public double Hvs => _hvs;

        /// <summary>View-space scale used for keyboard navigation speed.</summary>
        public float NavigationScale => Math.Max(EffectiveHalfViewSize(_picked.Z), 0.001f);

        /// <summary>World-space radius used to keep the orbit gizmo a stable screen size.</summary>
        public float PivotIndicatorScale => Math.Max(EffectiveHalfViewSize(WorldToView(_orbitPivot, _vtw).Z) * 0.3f, 0.001f);

        public OrbitCamera() { RebuildRot(); CalcViewVolume(); }

        // ─────────────────────────────────────────────────────────────────────
        // Setup
        // ─────────────────────────────────────────────────────────────────────

        public void SetViewportSize(int w, int h)
        {
            _vpW = w; _vpH = h;
            CalcViewVolume();
        }

        public void FitToCloud(float radius)
        {
            _sceneRadius  = radius > 0 ? radius : 50f;
            _hvs          = _sceneRadius;
            _trn          = Vector3.Zero;
            _orbitPivot   = Vector3.Zero;
            CalcViewVolume();
        }

        // ── Matrices for shader ───────────────────────────────────────────────

        public Matrix4 GetProjectionMatrix()
        {
            var p = new Matrix4();

            if (iez == 0f)
            {
                p.Row0 = new Vector4( 1f / hw,    0f,       0f,                    0f);
                p.Row1 = new Vector4( 0f,          1f / hh,  0f,                    0f);
                p.Row2 = new Vector4(-tsx / hw,   -tsy / hh, -2f / (zn - zf),       0f);
                p.Row3 = new Vector4( 0f,          0f,       (zn + zf) / (zn - zf), 1f);
            }
            else
            {
                if (MathF.Abs(iez) < 1e-6f) iez = 1e-6f;
                float ez = 1f / iez;
                p.Row0 = new Vector4( ez / hw,         0f,             0f,                                                0f);
                p.Row1 = new Vector4( 0f,               ez / hh,       0f,                                                0f);
                p.Row2 = new Vector4(-ez * tsx / hw,   -ez * tsy / hh, -(2f * ez - (zn + zf)) / (zn - zf),              -1f);
                p.Row3 = new Vector4( 0f,               0f,            -2f * (ez * (ez - (zn + zf)) + zn * zf) / (zn - zf), 0f);
            }
            return p;
        }

        public Matrix4 GetViewMatrix()
        {
            var wtvRot = new Matrix4(
                _vtw.M11, _vtw.M21, _vtw.M31, 0f,
                _vtw.M12, _vtw.M22, _vtw.M32, 0f,
                _vtw.M13, _vtw.M23, _vtw.M33, 0f,
                0f,        0f,       0f,        1f);

            var tPivot = Matrix4.CreateTranslation(-_trn);
            var vBase  = tPivot * wtvRot;

            if (iez == 0f) return vBase;

            float ez   = 1f / iez;
            var   tEye = Matrix4.CreateTranslation(-ez * tsx, -ez * tsy, -ez);
            return vBase * tEye;
        }

        // ── Mouse interactions - exactly 1:1 AdvancedZPR event handlers ───────

        /// <summary>
        /// Reads depth from depth buffer at a single pixel.
        /// Must be called from GL context. Before every MouseDown and MouseWheel.
        /// </summary>
        public void PickDepth(int mouseX, int mouseY)
        {
            float sd = 1f;
            GL.ReadPixels(mouseX, _vpH - mouseY, 1, 1,
                          PixelFormat.DepthComponent, PixelType.Float, ref sd);
            _pickedDepth = sd;

            float viewZ;
            if (sd >= 0.9999f)
            {
                viewZ = _picked.Z;   // background: reuse previous valid depth
            }
            else
            {
                float m33 = -(1f - zf * iez) / (zn - zf);
                viewZ = (sd + m33 * zn) / (sd * iez + m33);
            }

            _picked = ScreenToView(mouseX, mouseY, viewZ);
        }

        /// <summary>
        /// Reads depth from a NxN window and picks the closest point.
        /// Avoids background gaps between sparse cloud points.
        /// </summary>
        public void PickDepthWindow(int mouseX, int mouseY, int windowSize = 21)
        {
            float sd = ReadClosestDepthWindow(mouseX, mouseY, windowSize);
            _pickedDepth = sd;

            float viewZ;
            if (sd >= 0.9999f)
            {
                viewZ = _picked.Z;
            }
            else
            {
                float m33 = -(1f - zf * iez) / (zn - zf);
                viewZ = (sd + m33 * zn) / (sd * iez + m33);
            }

            _picked = ScreenToView(mouseX, mouseY, viewZ);
        }

        /// <summary>Sets the orbit center from the depth under a screen-space marker.</summary>
        public bool SetOrbitPivotFromScreen(int screenX, int screenY, int windowSize = 11)
        {
            float depth = ReadClosestDepthWindow(screenX, screenY, windowSize);
            if (depth >= 0.9999f)
                return false;

            float viewZ = DepthToViewZ(depth);
            _orbitPivot = ViewToWorld(ScreenToView(screenX, screenY, viewZ));
            return true;
        }

        /// <summary>Rotation - pixel delta. Orbits around _trn (world origin after FitToCloud).</summary>
        public void Rotate(float dx, float dy)
        {
            Matrix4 oldVtw = _vtw;
            Vector3 pivotView = WorldToView(_orbitPivot, oldVtw);

            _az -= dx * RotationSpeed;
            _el -= dy * RotationSpeed;
            if (ConstrainElev) _el = Math.Clamp(_el, -89f, 89f);
            RebuildRot();

            _trn = _orbitPivot - MulDir(pivotView, _vtw);
        }

        /// <summary>FPS style navigation in view space (W, A, S, D, Q, E).</summary>
        public void MoveFPS(float dx, float dy, float dz)
        {
            Vector3 deltaWorld = MulDir(new Vector3(dx, dy, dz), _vtw);
            _trn += deltaWorld;
            _orbitPivot += deltaWorld;
        }

        /// <summary>
        /// Pan - from/to screen coordinates.
        /// Uses pickedPointView.Z (= _picked.Z) exactly as in AdvancedZPR,
        /// so the picked point stays under the cursor in both ortho and perspective.
        /// In ortho iez=0 so viewZ has no effect on x/y.
        /// </summary>
        public void Pan(int fromX, int fromY, int toX, int toY)
        {
            float viewZ = _picked.Z;
            var vFrom  = ScreenToView(fromX, fromY, viewZ);
            var vTo    = ScreenToView(toX,   toY,   viewZ);
            var deltaV = vTo - vFrom;
            _trn -= MulDir(deltaV, _vtw);
        }

        /// <summary>
        /// Zoom towards the point under the mouse.
        /// Uses pickedPointView.Z (= _picked.Z) exactly as in AdvancedZPR.
        /// </summary>
        public void Zoom(int mouseX, int mouseY, float factor)
        {
            float viewZ = _picked.Z;
            Vector3 anchorWorld = ViewToWorld(ScreenToView(mouseX, mouseY, viewZ));
            _hvs = Math.Clamp(_hvs / factor, 0.001, 1_000_000.0);

            if (_vang != 0.0)
                viewZ /= factor;

            CalcViewVolume();
            _trn = anchorWorld - MulDir(ScreenToView(mouseX, mouseY, viewZ), _vtw);
            _picked = ScreenToView(mouseX, mouseY, viewZ);
        }

        /// <summary>
        /// Toggle ortho / perspective, preserving the point under the mouse.
        /// Uses pickedPointView.Z (= _picked.Z) exactly as in AdvancedZPR.
        /// </summary>
        public void ToggleProjection(int mouseX, int mouseY)
        {
            float viewZ = _picked.Z;
            Vector3 anchorWorld = ViewToWorld(ScreenToView(mouseX, mouseY, viewZ));

            if (_vang == 0.0)
            {
                double targetAngle = 45.0 * Math.PI / 180.0;
                float tanHalf = (float)Math.Tan(0.5 * targetAngle);
                _hvs = Math.Clamp(_hvs + viewZ * tanHalf, 0.001, 1_000_000.0);
                _vang = targetAngle;
            }
            else
            {
                _hvs = Math.Clamp(_hvs * (1.0 - viewZ * iez), 0.001, 1_000_000.0);
                _vang = 0.0;
            }

            CalcViewVolume();
            _trn = anchorWorld - MulDir(ScreenToView(mouseX, mouseY, viewZ), _vtw);
        }

        // ── Standard views and reset ──────────────────────────────────────────

        public void SetFrontView()    => ApplyView(0f,    0f);
        public void SetBackView()     => ApplyView(180f,  0f);
        public void SetRightView()    => ApplyView(90f,   0f);
        public void SetLeftView()     => ApplyView(-90f,  0f);
        public void SetTopView()      => ApplyView(0f,    89f);
        public void SetBottomView()   => ApplyView(0f,   -89f);
        public void SetIsometric()    => ApplyView(45f,   35.264f);

        public void ResetView(float cloudRadius = 50f)
        {
            _az   = 30f; _el = 25f;
            _trn  = Vector3.Zero;
            _orbitPivot = Vector3.Zero;
            _vang = 45.0 * Math.PI / 180.0;
            FitToCloud(cloudRadius);
        }

        // ── Hilton internal functions (private) - 1:1 AdvancedZPR ─────────────

        private void CalcViewVolume()
        {
            hw = hh = (float)_hvs;
            tsx = tsy = 0f;

            float depthExtent = MathF.Max(_sceneRadius * 2f, (float)_hvs * 8f);
            depthExtent = MathF.Max(depthExtent, MathF.Abs(WorldToView(_orbitPivot, _vtw).Z) + _sceneRadius * 2f);
            depthExtent = MathF.Max(depthExtent, MathF.Abs(_picked.Z) + _sceneRadius * 2f);
            zn = depthExtent;
            zf = -depthExtent;

            if (_vpW >= _vpH)
                hw *= (float)_vpW / _vpH;
            else
                hh *= (float)_vpH / _vpW;

            if (_vang == 0.0)
            {
                iez = 0f;
            }
            else
            {
                float eyeZ = Math.Min(hw, hh) / (float)Math.Tan(0.5 * _vang);
                iez = 1f / eyeZ;
                // Ensure near plane is not too close (use proportional distance to prevent depth precision loss)
                float kMinNear = eyeZ * 0.05f;
                if (zn > eyeZ - kMinNear)
                    zn = eyeZ - kMinNear;
            }
        }

        /// <summary>
        /// Hilton ScreenToView - exactly the AdvancedZPR code.
        /// Pixel (px,py) -> view-space point on the viewZ plane.
        /// </summary>
        private Vector3 ScreenToView(int px, int py, float viewZ)
        {
            float pixToView = hw * 2f / _vpW;
            float x = px * pixToView - hw;
            float y = -(py * pixToView - hh);

            // Perspective correction (for ortho, iez=0 -> no effect)
            x += -x * viewZ * iez + tsx * viewZ;
            y += -y * viewZ * iez + tsy * viewZ;

            return new Vector3(x, y, viewZ);
        }

        private void RebuildRot()
        {
            float azR = _az * MathF.PI / 180f;
            float elR = _el * MathF.PI / 180f;
            float cz = MathF.Cos(azR), sz = MathF.Sin(azR);
            float cx = MathF.Cos(elR), sx = MathF.Sin(elR);

            // Rz(azimuth) * Rx(elevation) - identical to AdvancedZPR code
            _vtw = new Matrix4(
                 cz,       sz,       0f,  0f,
                -sz * cx,  cz * cx,  sx,  0f,
                 sz * sx, -cz * sx,  cx,  0f,
                 0f,       0f,       0f,  1f);
        }

        private void ApplyView(float az, float el)
        {
            float pivotZ = WorldToView(_orbitPivot, _vtw).Z;
            _az = az;
            _el = ConstrainElev ? Math.Clamp(el, -89f, 89f) : el;
            RebuildRot();
            _trn = _orbitPivot - MulDir(new Vector3(0f, 0f, pivotZ), _vtw);
        }

        private float ReadClosestDepthWindow(int mouseX, int mouseY, int windowSize)
        {
            int half = windowSize / 2;
            int glY = _vpH - 1 - mouseY;
            int startX = Math.Max(0, mouseX - half);
            int startY = Math.Max(0, glY - half);
            int readW  = Math.Min(windowSize, _vpW - startX);
            int readH  = Math.Min(windowSize, _vpH - startY);

            float sd = 1f;
            if (readW > 0 && readH > 0)
            {
                int needed = readW * readH;
                if (_depthWindow.Length < needed)
                    _depthWindow = new float[needed];
                GL.ReadPixels(startX, startY, readW, readH,
                              PixelFormat.DepthComponent, PixelType.Float, _depthWindow);
                for (int i = 0; i < needed; i++)
                    if (_depthWindow[i] < 0.9999f && _depthWindow[i] < sd) sd = _depthWindow[i];
            }

            return sd;
        }

        private float DepthToViewZ(float screenDepth)
        {
            float m33 = -(1f - zf * iez) / (zn - zf);
            return (screenDepth + m33 * zn) / (screenDepth * iez + m33);
        }

        private float EffectiveHalfViewSize(float viewZ)
        {
            if (_vang == 0.0)
                return (float)_hvs;

            return MathF.Max((float)_hvs * (1f - viewZ * iez), 0.001f);
        }

        private Vector3 ViewToWorld(Vector3 viewPoint) => _trn + MulDir(viewPoint, _vtw);

        private Vector3 WorldToView(Vector3 worldPoint, Matrix4 vtw)
        {
            Vector3 d = worldPoint - _trn;
            return new Vector3(
                d.X * vtw.M11 + d.Y * vtw.M12 + d.Z * vtw.M13,
                d.X * vtw.M21 + d.Y * vtw.M22 + d.Z * vtw.M23,
                d.X * vtw.M31 + d.Y * vtw.M32 + d.Z * vtw.M33);
        }

        // Row-vector x matrix 3x3 part (direction only, no translation)
        private static Vector3 MulDir(Vector3 v, Matrix4 m) => new(
            v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31,
            v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32,
            v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33);
    }
}
