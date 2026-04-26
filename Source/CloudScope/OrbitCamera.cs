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
        // Exactly matches AdvancedZPR calcViewVolume variables
        private float hw, hh;      // half view widths (aspect-corrected)
        private float zn, zf;      // near / far in Hilton view-space
        private float iez;         // 1/eyeZ  (0 = orthographic)
        private float tsx, tsy;    // view-translation (always 0)

        // ── View-to-World transformation (Hilton) ─────────────────────────────
        private Matrix4 _vtw = Matrix4.Identity;   // view->world rotation
        private Vector3 _trn = Vector3.Zero;        // pivot (viewToWorldTrn)

        // ── Euler angles ──────────────────────────────────────────────────────
        private float _az = 30f;
        private float _el = 25f;

        // ── Zoom / projection ─────────────────────────────────────────────────
        private double _hvs  = 50.0;                          // halfViewSize
        private double _vang = 45.0 * Math.PI / 180.0;        // 0 = ortho

        // ── Fixed scene depth range (does not change with zoom) ───────────────
        // Exactly like AdvancedZPR: zn/zf comes from scene size,
        // NOT from halfViewSize. This is critical for PickDepth!
        private float _sceneRadius = 50f;

        // ── Viewport size ─────────────────────────────────────────────────────
        private int _vpW = 1600, _vpH = 900;

        // ── Pick state (PickDepth writes, Pan/Zoom reads) ─────────────────────
        private Vector3 _picked = Vector3.Zero;
        private float   _pickedDepth = 1f;

        // ── Configurable parameters ───────────────────────────────────────────
        public float RotationSpeed  { get; set; } = 0.5f;
        public bool  ConstrainElev  { get; set; } = true;

        /// <summary>Returns the world coordinates of the current focus/rotation center.</summary>
        public Vector3 Pivot => _trn;

        /// <summary>The current "half screen size" at the pivot distance.</summary>
        public double Hvs => _hvs;

        public OrbitCamera() { RebuildRot(); CalcViewVolume(); }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // BeĂˇllĂ­tĂˇs
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public void SetViewportSize(int w, int h)
        {
            _vpW = w; _vpH = h;
            CalcViewVolume();
        }

        /// <summary>
        /// halfViewSize = cloud radius; zn/zf is fixed to scene scale.
        /// </summary>
        public void FitToCloud(float radius)
        {
            _sceneRadius  = radius > 0 ? radius : 50f;
            _hvs          = _sceneRadius;
            _trn          = Vector3.Zero;
            CalcViewVolume();
        }

        // ── Matrices for shader ───────────────────────────────────────────────

        /// <summary>
        /// Hilton projection matrix - exactly the calcProjection matrix from AdvancedZPR.
        /// OpenTK rows = GL columns (when transpose:false is used).
        /// </summary>
        public Matrix4 GetProjectionMatrix()
        {
            var p = new Matrix4();

            if (iez == 0f)
            {
                // Orthographic (AdvancedZPR Orthographic branch)
                p.Row0 = new Vector4( 1f / hw,    0f,       0f,                    0f);
                p.Row1 = new Vector4( 0f,          1f / hh,  0f,                    0f);
                p.Row2 = new Vector4(-tsx / hw,   -tsy / hh, -2f / (zn - zf),       0f);
                p.Row3 = new Vector4( 0f,          0f,       (zn + zf) / (zn - zf), 1f);
            }
            else
            {
                // Perspective (AdvancedZPR Perspective branch)
                if (MathF.Abs(iez) < 1e-6f) iez = 1e-6f;
                float ez = 1f / iez;
                p.Row0 = new Vector4( ez / hw,         0f,             0f,                                                0f);
                p.Row1 = new Vector4( 0f,               ez / hh,       0f,                                                0f);
                p.Row2 = new Vector4(-ez * tsx / hw,   -ez * tsy / hh, -(2f * ez - (zn + zf)) / (zn - zf),              -1f);
                p.Row3 = new Vector4( 0f,               0f,            -2f * (ez * (ez - (zn + zf)) + zn * zf) / (zn - zf), 0f);
            }
            return p;
        }

        /// <summary>
        /// View matrix = Hilton calcProjection Modelview stack.
        /// Ortho:       worldToViewRot * Translate(-trn)
        /// Perspective: Translate(0,0,-ez) * worldToViewRot * Translate(-trn)
        /// </summary>
        public Matrix4 GetViewMatrix()
        {
            // worldToViewRot: exactly the construct from AdvancedZPR calcProjection
            var wtvRot = new Matrix4(
                _vtw.M11, _vtw.M21, _vtw.M31, 0f,
                _vtw.M12, _vtw.M22, _vtw.M32, 0f,
                _vtw.M13, _vtw.M23, _vtw.M33, 0f,
                0f,        0f,       0f,        1f);

            var tPivot = Matrix4.CreateTranslation(-_trn);
            // Correct order in OpenTK so that GLSL receives (wtvRot^T * tPivot^T),
            // which matches the fixed-pipeline order (Rotate * Translate).
            var vBase  = tPivot * wtvRot;

            if (iez == 0f) return vBase;

            // Perspective: GL.Translate(-ez*tsx, -ez*tsy, -ez) from AdvancedZPR
            float ez   = 1f / iez;
            var   tEye = Matrix4.CreateTranslation(-ez * tsx, -ez * tsy, -ez);
            return vBase * tEye;
        }

        // ── Mouse interactions - exactly 1:1 AdvancedZPR event handlers ───────

        /// <summary>
        /// Reads depth from depth buffer.
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
                viewZ = _picked.Z;   // background: previous valid depth
            }
            else
            {
                // Hilton formula: window depth -> view-space Z
                float m33 = -(1f - zf * iez) / (zn - zf);
                viewZ = (sd + m33 * zn) / (sd * iez + m33);
            }

            _picked = ScreenToView(mouseX, mouseY, viewZ);
        }

        public void PickDepthWindow(int mouseX, int mouseY, int windowSize = 21)
        {
            // We read a window around the target to ensure we hit a point cloud 
            // point instead of the empty background gaps between points.
            int half = windowSize / 2;
            
            int startX = Math.Max(0, mouseX - half);
            int startY = Math.Max(0, _vpH - mouseY - half);
            
            // Adjust width/height if out of bounds
            int readW = Math.Min(windowSize, _vpW - startX);
            int readH = Math.Min(windowSize, _vpH - startY);

            float sd = 1f;
            if (readW > 0 && readH > 0)
            {
                float[] depthData = new float[readW * readH];
                GL.ReadPixels(startX, startY, readW, readH,
                              PixelFormat.DepthComponent, PixelType.Float, depthData);

                // Find the closest point (minimum depth) that is not the background
                foreach (float d in depthData)
                {
                    if (d < 0.9999f && d < sd)
                    {
                        sd = d;
                    }
                }
            }
            
            _pickedDepth = sd;

            float viewZ;
            if (sd >= 0.9999f)
            {
                viewZ = _picked.Z;   // background: previous valid depth
            }
            else
            {
                // Hilton formula: window depth -> view-space Z
                float m33 = -(1f - zf * iez) / (zn - zf);
                viewZ = (sd + m33 * zn) / (sd * iez + m33);
            }

            _picked = ScreenToView(mouseX, mouseY, viewZ);

            // Automatikus fĂłkusz: a pivotot rĂˇtoljuk a kivĂˇlasztott pontra.
            // ĂŤgy a forgatĂˇs a pont kĂ¶rĂĽl tĂ¶rtĂ©nik, Ă©s a vetĂ­tĂ©svĂˇltĂˇs/zoom teljesen stabil lesz.
            FocusOnPickedPoint();
        }

        /// <summary>
        /// Ăthelyezi a forgatĂˇsi/skĂˇlĂˇzĂˇsi kĂ¶zĂ©ppontot (pivot) a kivĂˇlasztott pontra.
        /// A kĂ©pernyĹ‘n a jelenet lĂˇtszĂłlag nem mozdul, mert a _hvs arĂˇnyosan korrigĂˇlĂłdik.
        /// </summary>
        private void FocusOnPickedPoint()
        {
            float zToFocus = _picked.Z;
            
            // If in perspective view, don't allow focus behind the camera
            if (iez > 0f)
            {
                float eyeZ = 1f / iez;
                if (zToFocus > eyeZ * 0.9f)
                    zToFocus = eyeZ * 0.9f;
            }

            if (MathF.Abs(zToFocus) < 1e-4f) return;

            // Effective size at the focus point
            double effectiveHvs = _hvs * (1.0 - zToFocus * iez);
            if (effectiveHvs < 0.001) effectiveHvs = 0.001;

            // Shift pivot
            _trn += MulDir(new Vector3(0, 0, zToFocus), _vtw);
            _hvs = effectiveHvs;
            
            // The point's new viewZ is now 0
            _picked.Z -= zToFocus;

            CalcViewVolume();
        }

        /// <summary>Rotation - pixel delta. Orbits around the picked point (_trn).</summary>
        public void Rotate(float dx, float dy)
        {
            _az -= dx * RotationSpeed;
            _el -= dy * RotationSpeed;
            if (ConstrainElev) _el = Math.Clamp(_el, -89f, 89f);
            RebuildRot();
        }

        /// <summary>FPS style navigation in view space (W, A, S, D, Q, E).</summary>
        public void MoveFPS(float dx, float dy, float dz)
        {
            // In orthographic (parallel) view, translating along Z does not change apparent size.
            // Instead, we convert the dz movement into a "zoom" function (scaling the view volume).
            if (_vang == 0.0 && dz != 0)
            {
                // Convert dz to a relative scale factor.
                // dz negative (W) -> decrease hvs (zoom in)
                // dz positive (S) -> increase hvs (zoom out)
                float scaleFactor = 1.0f + (dz / (float)_hvs);
                if (scaleFactor < 0.1f) scaleFactor = 0.1f;
                
                _hvs = Math.Clamp(_hvs * scaleFactor, 0.001, 1_000_000.0);
                CalcViewVolume();
                
                // Zero out dz to prevent unnecessary Z translation
                dz = 0f;
            }

            // In view space: +X is right, +Y is up, +Z is backward.
            // dx: left/right, dy: up/down, dz: forward/backward
            _trn += MulDir(new Vector3(dx, dy, dz), _vtw);
        }

        /// <summary>Pan - from/to screen coordinates.</summary>
        public void Pan(int fromX, int fromY, int toX, int toY)
        {
            // In perspective the picked depth controls the pan scale: points at the
            // picked depth stay exactly under the cursor.  In ortho iez=0 so viewZ
            // has no effect on x/y and both give identical results.
            float viewZ = _picked.Z;
            var vFrom  = ScreenToView(fromX, fromY, viewZ);
            var vTo    = ScreenToView(toX,   toY,   viewZ);
            var deltaV = vTo - vFrom;
            _trn -= MulDir(deltaV, _vtw);
        }

        /// <summary>Zoom towards the point under the mouse.</summary>
        public void Zoom(int mouseX, int mouseY, float factor)
        {
            var before = ScreenToView(mouseX, mouseY, 0f);
            _hvs = Math.Clamp(_hvs / factor, 0.001, 1_000_000.0);
            CalcViewVolume();
            var after  = ScreenToView(mouseX, mouseY, 0f);
            _trn -= MulDir(after - before, _vtw);
        }

        /// <summary>Toggle ortho / perspective, preserving the point under the mouse.</summary>
        public void ToggleProjection(int mouseX, int mouseY)
        {
            var before = ScreenToView(mouseX, mouseY, 0f);
            _vang = _vang == 0.0 ? 45.0 * Math.PI / 180.0 : 0.0;
            CalcViewVolume();
            var after  = ScreenToView(mouseX, mouseY, 0f);
            _trn -= MulDir(after - before, _vtw);
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
            _vang = 45.0 * Math.PI / 180.0;
            FitToCloud(cloudRadius);     // Restore halfViewSize + sceneRadius
        }

        // ── Hilton internal functions (private) - 1:1 AdvancedZPR ─────────────

        private void CalcViewVolume()
        {
            hw = hh = (float)_hvs;
            tsx = tsy = 0f;

            // Because we move the pivot point to the picked points (which can be on the edge of the cloud),
            // the clipping planes must reach twice as far so the far end of the cloud is not clipped.
            zn = _sceneRadius * 2f;
            zf = -_sceneRadius * 2f;

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
                const float kMinNear = 1e-3f;
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
            _az  = az;
            _el  = ConstrainElev ? Math.Clamp(el, -89f, 89f) : el;
            _trn = Vector3.Zero;
            RebuildRot();
        }

        // Row-vector x matrix 3x3 part (direction only, no translation)
        private static Vector3 MulDir(Vector3 v, Matrix4 m) => new(
            v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31,
            v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32,
            v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33);
    }
}

