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

        // ── Smooth transition state ───────────────────────────────────────────
        private float   _txStartAz, _txTargetAz;
        private float   _txStartEl, _txTargetEl;
        private double  _txStartHvs, _txTargetHvs;
        private Vector3 _txStartTrn,   _txTargetTrn;
        private Vector3 _txStartPivot, _txTargetPivot;
        private float   _txTime, _txDur;
        private bool    _txActive;

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

        /// <summary>Same as PivotIndicatorScale but for an arbitrary world position (e.g. display pivot).</summary>
        public float PivotIndicatorScaleAt(Vector3 worldPos) => Math.Max(EffectiveHalfViewSize(WorldToView(worldPos, _vtw).Z) * 0.3f, 0.001f);

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

        /// <summary>
        /// Sets the orbit center from the depth under a screen-space marker.
        /// If no geometry is found the pivot is projected onto a plane at the
        /// current orbit-pivot depth so orbiting stays sensible even when the
        /// user clicks on empty sky.
        /// </summary>
        public bool SetOrbitPivotFromScreen(int screenX, int screenY, int windowSize = 11)
        {
            float depth = ReadClosestDepthWindow(screenX, screenY, windowSize);
            if (depth < 0.9999f)
            {
                _orbitPivot = ViewToWorld(ScreenToView(screenX, screenY, DepthToViewZ(depth)));
                return true;
            }

            // Fallback: no geometry hit — project cursor at current pivot depth
            float pivotViewZ = WorldToView(_orbitPivot, _vtw).Z;
            _orbitPivot = ViewToWorld(ScreenToView(screenX, screenY, pivotViewZ));
            return false;
        }

        /// <summary>
        /// Rotation via pixel delta — CloudCompare turntable style (Z-lock):
        ///   horizontal drag → azimuth around world-Z
        ///   vertical drag   → elevation tilt around current view-right
        /// Fixed speed (RotationSpeed deg/pixel) independent of zoom level,
        /// so Z-axis rotation stays responsive at every zoom and elevation.
        /// </summary>
        public void Rotate(float dx, float dy)
        {
            Vector3 pivotView = WorldToView(_orbitPivot, _vtw);

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
        /// Uses pickedPointView.Z (= _picked.Z) so the picked point stays under
        /// the cursor in both ortho and perspective.
        /// The orbit pivot is translated by the same world delta so that subsequent
        /// orbiting (without a new click) stays coherent after a pan.
        /// </summary>
        public void Pan(int fromX, int fromY, int toX, int toY)
        {
            float viewZ     = _picked.Z;
            var   vFrom     = ScreenToView(fromX, fromY, viewZ);
            var   vTo       = ScreenToView(toX,   toY,   viewZ);
            var   worldDelta = MulDir(vTo - vFrom, _vtw);
            _trn        -= worldDelta;
            _orbitPivot -= worldDelta; // keep pivot view-relative so orbit after pan is stable
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
            _sceneRadius = cloudRadius > 0 ? cloudRadius : 50f;
            _vang = 45.0 * Math.PI / 180.0;
            CalcViewVolume();
            BeginTransition(30f, 25f, _sceneRadius, Vector3.Zero, Vector3.Zero, dur: 0.50f);
        }

        // ── Smooth animated transitions ───────────────────────────────────────

        /// <summary>Queues a smooth fly-to transition. Any existing transition is replaced.</summary>
        public void BeginTransition(float targetAz, float targetEl, double targetHvs,
                                    Vector3 targetTrn, Vector3 targetPivot, float dur = 0.32f)
        {
            _txStartAz    = _az;          _txTargetAz    = targetAz;
            _txStartEl    = _el;          _txTargetEl    = ConstrainElev ? Math.Clamp(targetEl, -89f, 89f) : targetEl;
            _txStartHvs   = _hvs;         _txTargetHvs   = Math.Max(targetHvs, 0.001);
            _txStartTrn   = _trn;         _txTargetTrn   = targetTrn;
            _txStartPivot = _orbitPivot;  _txTargetPivot = targetPivot;
            _txTime = 0f; _txDur = MathF.Max(dur, 0.001f);
            _txActive = true;
        }

        /// <summary>Advances the transition by dt seconds. Call once per frame in UpdateFrame.</summary>
        public bool TickTransition(float dt)
        {
            if (!_txActive) return false;
            _txTime = Math.Min(_txTime + dt, _txDur);
            float t = _txTime / _txDur;
            float s = t * t * (3f - 2f * t);   // smoothstep — ease in/out

            // Shortest-path azimuth (handles wrap-around 350 -> 10 correctly)
            float azDiff = ((_txTargetAz - _txStartAz) % 360f + 540f) % 360f - 180f;
            _az = _txStartAz + azDiff * s;
            _el = _txStartEl + (_txTargetEl - _txStartEl) * s;

            // Log-space hvs: perceptually uniform zoom speed regardless of magnitude
            _hvs = Math.Exp(Math.Log(_txStartHvs) +
                            (Math.Log(_txTargetHvs) - Math.Log(_txStartHvs)) * s);

            _trn        = Vector3.Lerp(_txStartTrn,   _txTargetTrn,   s);
            _orbitPivot = Vector3.Lerp(_txStartPivot, _txTargetPivot, s);
            RebuildRot();
            CalcViewVolume();

            if (t >= 1f) _txActive = false;
            return true;
        }

        /// <summary>Immediately stops any in-progress transition (call on mouse down).</summary>
        public void CancelTransition() => _txActive = false;

        /// <summary>True while a smooth fly-to animation is in progress.</summary>
        public bool IsTransitioning => _txActive;

        /// <summary>
        /// Reads depth under the cursor, then smoothly flies the camera to that
        /// world-space point, centering and zooming so the feature fills ~40% of view.
        /// No-op if no geometry is under the cursor.
        /// </summary>
        public void FocusOnCursor(int screenX, int screenY)
        {
            float depth = ReadClosestDepthWindow(screenX, screenY, 21);
            if (depth >= 0.9999f) return;

            float   viewZ      = DepthToViewZ(depth);
            Vector3 focusWorld = ViewToWorld(ScreenToView(screenX, screenY, viewZ));

            // Target hvs: fill ~40% of view with the region near the focus point
            double targetHvs = _vang != 0.0
                ? Math.Max(Math.Abs(viewZ) * Math.Tan(0.5 * _vang) * 0.40, 0.01)
                : Math.Max(_hvs * 0.25, 0.01);

            // Target trn: centre the focus point horizontally on screen
            float   pivotViewZ = WorldToView(focusWorld, _vtw).Z;
            Vector3 targetTrn  = focusWorld - MulDir(new Vector3(0f, 0f, pivotViewZ), _vtw);

            BeginTransition(_az, _el, targetHvs, targetTrn, focusWorld, dur: 0.40f);
        }

        // ── Public coordinate-conversion helpers (for labeling / selection) ────

        /// <summary>Current viewport width in pixels.</summary>
        public int ViewportWidth => _vpW;

        /// <summary>Current viewport height in pixels.</summary>
        public int ViewportHeight => _vpH;

        /// <summary>
        /// Converts a screen pixel to a world-space point by reading the depth buffer.
        /// Must be called from the GL context thread.
        /// Returns (worldPoint, hitGeometry).
        /// </summary>
        public (Vector3 point, bool hit) ScreenToWorldPoint(int screenX, int screenY, int windowSize = 11)
        {
            float depth = ReadClosestDepthWindow(screenX, screenY, windowSize);
            if (depth >= 0.9999f)
            {
                // No geometry hit — project at current picked depth
                return (ViewToWorld(ScreenToView(screenX, screenY, _picked.Z)), false);
            }

            float viewZ = DepthToViewZ(depth);
            return (ViewToWorld(ScreenToView(screenX, screenY, viewZ)), true);
        }

        /// <summary>
        /// Unprojects a screen pixel to world-space at a given view-space Z depth.
        /// Does NOT read the depth buffer — used for smooth handle dragging at a fixed depth.
        /// </summary>
        public Vector3 ScreenToWorldAtDepth(int screenX, int screenY, float viewZ)
        {
            return ViewToWorld(ScreenToView(screenX, screenY, viewZ));
        }

        /// <summary>
        /// Returns the view-space Z of a world point (how deep it is in front of the camera).
        /// Used to cache depth for handle dragging.
        /// </summary>
        public float WorldToViewZ(Vector3 worldPoint)
        {
            return WorldToView(worldPoint, _vtw).Z;
        }

        /// <summary>
        /// Projects a world-space point to Normalized Device Coordinates (NDC).
        /// NDC x,y ∈ [-1,1], z = depth.  Returns (ndc, behindCamera).
        /// This avoids reading GL state — purely CPU math on cached matrices.
        /// </summary>
        public (Vector3 ndc, bool behind) WorldToNDC(Vector3 worldPoint)
        {
            // World → View
            Vector3 vp = WorldToView(worldPoint, _vtw);

            // Perspective: point is behind the eye when viewZ > 1/iez
            bool behind = false;
            if (iez != 0f)
            {
                float eyeZ = 1f / iez;
                if (vp.Z >= eyeZ) behind = true;
            }

            // View → Clip  (using the same Hilton projection as GetProjectionMatrix)
            float clipX, clipY, clipZ, clipW;
            if (iez == 0f)
            {
                // Orthographic
                clipX = vp.X / hw;
                clipY = vp.Y / hh;
                clipZ = (-2f * vp.Z - (zn + zf)) / (zn - zf);
                clipW = 1f;
            }
            else
            {
                float ez = 1f / iez;
                // View matrix includes tEye = Translate(0,0,-ez), so effective viewZ = vp.Z - ez
                // clipW = -(vp.Z - ez) = ez - vp.Z  (not just -vp.Z)
                clipX = ez * vp.X / hw;
                clipY = ez * vp.Y / hh;
                clipZ = (-(2f * ez - (zn + zf)) * (vp.Z - ez) - 2f * (ez * (ez - (zn + zf)) + zn * zf)) / (zn - zf);
                clipW = ez - vp.Z;
            }

            // Clip → NDC
            if (MathF.Abs(clipW) < 1e-7f)
                return (Vector3.Zero, true);

            return (new Vector3(clipX / clipW, clipY / clipW, clipZ / clipW), behind);
        }

        /// <summary>
        /// Projects a world-space point to screen pixel coordinates.
        /// Returns (px, py, behindCamera).
        /// </summary>
        public (float px, float py, bool behind) WorldToScreen(Vector3 worldPoint)
        {
            var (ndc, behind) = WorldToNDC(worldPoint);
            float px = (ndc.X + 1f) * 0.5f * _vpW;
            float py = (1f - ndc.Y) * 0.5f * _vpH;
            return (px, py, behind);
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

        private static Matrix4 BuildRot(float az, float el)
        {
            float azR = az * MathF.PI / 180f;
            float elR = el * MathF.PI / 180f;
            float cz = MathF.Cos(azR), sz = MathF.Sin(azR);
            float cx = MathF.Cos(elR), sx = MathF.Sin(elR);
            return new Matrix4(
                 cz,       sz,       0f,  0f,
                -sz * cx,  cz * cx,  sx,  0f,
                 sz * sx, -cz * sx,  cx,  0f,
                 0f,       0f,       0f,  1f);
        }

        private void RebuildRot() => _vtw = BuildRot(_az, _el);

        private void ApplyView(float az, float el)
        {
            el = ConstrainElev ? Math.Clamp(el, -89f, 89f) : el;
            float   pivotZ    = WorldToView(_orbitPivot, _vtw).Z;
            Matrix4 targetVtw = BuildRot(az, el);
            Vector3 targetTrn = _orbitPivot - MulDir(new Vector3(0f, 0f, pivotZ), targetVtw);
            BeginTransition(az, el, _hvs, targetTrn, _orbitPivot);
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

        // ── Selection helpers ─────────────────────────────────────────────────

        /// <summary>Sets the orbit pivot to an explicit world-space point (no depth read).</summary>
        public void SetOrbitPivot(Vector3 worldPos) => _orbitPivot = worldPos;

        /// <summary>Camera right axis in world space.</summary>
        public Vector3 CameraRight   => new(_vtw.M11, _vtw.M21, _vtw.M31);
        /// <summary>Camera up axis in world space.</summary>
        public Vector3 CameraUp      => new(_vtw.M12, _vtw.M22, _vtw.M32);
        /// <summary>Camera forward direction in world space (into the scene).</summary>
        public Vector3 CameraForward => new(-_vtw.M13, -_vtw.M23, -_vtw.M33);

        /// <summary>
        /// Picks the closest world-space point under a screen pixel using depth buffer.
        /// Falls back to current pivot when no geometry is hit.
        /// </summary>
        public bool TryPickWorldPoint(int sx, int sy, int window, out Vector3 worldPt)
        {
            float d = ReadClosestDepthWindow(sx, sy, window);
            if (d >= 0.9999f) { worldPt = _orbitPivot; return false; }
            worldPt = ViewToWorld(ScreenToView(sx, sy, DepthToViewZ(d)));
            return true;
        }

        /// <summary>
        /// Projects a world-space point to screen pixels (top-left origin).
        /// Returns false when the point is behind the camera.
        /// </summary>
        public bool ProjectWorldToScreen(Vector3 worldPt, out float sx, out float sy)
        {
            Matrix4 view = GetViewMatrix();
            Matrix4 proj = GetProjectionMatrix();
            // row-vector convention: clip = worldPt * view * proj
            float vx = worldPt.X * view.M11 + worldPt.Y * view.M21 + worldPt.Z * view.M31 + view.M41;
            float vy = worldPt.X * view.M12 + worldPt.Y * view.M22 + worldPt.Z * view.M32 + view.M42;
            float vz = worldPt.X * view.M13 + worldPt.Y * view.M23 + worldPt.Z * view.M33 + view.M43;
            float vw = worldPt.X * view.M14 + worldPt.Y * view.M24 + worldPt.Z * view.M34 + view.M44;
            float cx = vx * proj.M11 + vy * proj.M21 + vz * proj.M31 + vw * proj.M41;
            float cy = vx * proj.M12 + vy * proj.M22 + vz * proj.M32 + vw * proj.M42;
            float cw = vx * proj.M14 + vy * proj.M24 + vz * proj.M34 + vw * proj.M44;
            if (cw <= 0f) { sx = sy = 0f; return false; }
            sx = (cx / cw + 1f) * 0.5f * _vpW;
            sy = (1f - cy / cw) * 0.5f * _vpH;
            return true;
        }

        /// <summary>
        /// Returns the world-space distance between the given world center and the screen
        /// pixel (sx,sy), measured in the view plane at the center's depth.
        /// Used to convert a sphere-radius drag distance to world scale.
        /// </summary>
        public float ScreenToWorldRadius(Vector3 worldCenter, int sx, int sy)
        {
            Vector3 vc = WorldToView(worldCenter, _vtw);
            Vector3 vm = ScreenToView(sx, sy, vc.Z);
            return (vm - vc).Length;
        }
    }
}
