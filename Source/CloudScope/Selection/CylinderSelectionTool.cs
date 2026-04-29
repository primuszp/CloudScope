using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space upright cylinder (axis = world Z) — two-phase workflow matching the box tool.
    ///
    /// Phase 1 – Drawing:
    ///   Left-drag on screen defines the base circle (center + radius).
    ///   Mouse-up triggers FinalizeDiskFromScreen (called by viewer), which places a flat disk
    ///   on the surface and auto-begins extruding upward.
    ///
    /// Phase 1b – Extruding (IsExtruding == true, Phase == Editing):
    ///   Mouse move live-adjusts HalfHeight along world Z.
    ///   Left-click confirms.
    ///
    /// Phase 2 – Editing:
    ///   7 handles: center(0), top(1), bottom(2), ±local-X(3/4), ±local-Z(5/6).
    ///   G/S/R + X/Y/Z axis constraints, scroll = fine-tune.
    /// </summary>
    public sealed class CylinderSelectionTool : SelectionToolBase
    {
        public override SelectionToolType ToolType    => SelectionToolType.Cylinder;
        public override int               HandleCount => 7;

        // HasVolume false while extruding prevents premature preview/label
        public override bool HasVolume =>
            IsEditing && !_extruding && Radius > 1e-4f && HalfHeight > 1e-4f;

        public float      Radius     { get; set; }
        public float      HalfHeight { get; set; }
        // Default rotation: local Y → world Z (cylinder stands upright in Z-up scene)
        private static readonly Quaternion UprightRot =
            Quaternion.FromAxisAngle(Vector3.UnitX, MathF.PI / 2f);

        public Quaternion Rotation = Quaternion.FromAxisAngle(Vector3.UnitX, MathF.PI / 2f);

        // Drawing screen coords
        public int StartX, StartY, EndX, EndY;

        // Extrude state
        public bool IsExtruding  => _extruding;
        private bool    _extruding;
        private float   _extrudeBaseZ;      // world Z of the placed disk surface
        private Vector2 _extrudeScreenDir;  // screen-space direction of world +Z

        // Local axes derived from rotation
        public  Vector3 Axis   => Vector3.Transform(Vector3.UnitY, Rotation);
        private Vector3 LocalX => Vector3.Transform(Vector3.UnitX, Rotation);
        private Vector3 LocalZ => Vector3.Transform(Vector3.UnitZ, Rotation);

        // Drag / edit state
        private float      _editStartRadius;
        private float      _editStartHalfHeight;
        private Quaternion _editStartRotation;
        private Vector2    _radialScreenDir;

        // ── Placement ─────────────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            StartX = EndX = mx;
            StartY = EndY = my;
            Radius     = 0f;
            HalfHeight = 0f;
            Rotation   = Quaternion.FromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
            _extruding = false;
            Phase      = ToolPhase.Drawing;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (Phase == ToolPhase.Drawing) { EndX = mx; EndY = my; }
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (Phase != ToolPhase.Drawing) return;
            EndX = mx; EndY = my;
            if (Math.Abs(EndX - StartX) < 5 && Math.Abs(EndY - StartY) < 5)
                Phase = ToolPhase.Idle;
            // FinalizeDiskFromScreen called by viewer
        }

        /// <summary>
        /// Projects the drawn screen circle onto the point cloud surface,
        /// places a flat disk, then begins height extrude along world Z.
        /// Called by viewer after OnMouseUp while Phase == Drawing.
        /// </summary>
        public void FinalizeDiskFromScreen(OrbitCamera cam)
        {
            int x0 = Math.Min(StartX, EndX), x1 = Math.Max(StartX, EndX);
            int y0 = Math.Min(StartY, EndY), y1 = Math.Max(StartY, EndY);
            int cxs = (x0 + x1) / 2, cys = (y0 + y1) / 2;

            var (centerWorld, hit) = cam.ScreenToWorldPoint(cxs, cys, 11);
            if (!hit) { Phase = ToolPhase.Idle; return; }

            float refViewZ = cam.WorldToViewZ(centerWorld);
            Center = cam.ScreenToWorldAtDepth(cxs, cys, refViewZ);

            // Radius = horizontal world distance from center to right edge
            Vector3 edge = cam.ScreenToWorldAtDepth(x1, cys, refViewZ);
            Radius = MathF.Max((edge - Center).Length, 0.001f);

            // Rotation stays upright (local Y = world Z, set in OnMouseDown)
            HalfHeight    = 0.001f;
            _extrudeBaseZ = Center.Z;

            // Screen-space direction of world +Z at the center point:
            // project center and center+Z to screen, compare pixel positions
            var (sx0, sy0, _) = cam.WorldToScreen(Center);
            var (sx1, sy1, _) = cam.WorldToScreen(Center + new Vector3(0f, 0f, 1f));
            float ddx = sx1 - sx0, ddy = sy1 - sy0;
            float dlen = MathF.Sqrt(ddx * ddx + ddy * ddy);
            // If world Z is nearly perpendicular to screen (top-down view), use screen -Y
            _extrudeScreenDir = dlen > 0.5f
                ? new Vector2(ddx / dlen, ddy / dlen)
                : new Vector2(0f, -1f);

            Phase      = ToolPhase.Editing;
            _extruding = true;
        }

        /// <summary>Mouse move while extruding — adjusts HalfHeight along world Z.</summary>
        public void UpdateExtrude(int mx, int my, OrbitCamera cam)
        {
            if (!_extruding) return;

            // Project mouse delta (from draw-end position) onto the screen-space Z direction
            int dsx = mx - EndX, dsy = my - EndY;
            float proj = dsx * _extrudeScreenDir.X + dsy * _extrudeScreenDir.Y;

            // Convert screen pixels → world units using NavigationScale
            float worldPerPixel = cam.NavigationScale / (cam.ViewportHeight * 0.5f);
            float height = MathF.Max(-proj * worldPerPixel * 100f, 0.001f);
            // negative proj because moving toward Z+ = moving opposite to screen dir

            HalfHeight = height;
            Center = new Vector3(Center.X, Center.Y, _extrudeBaseZ + HalfHeight);
        }

        /// <summary>Left-click during extrude confirms height.</summary>
        public void ConfirmExtrude() => _extruding = false;

        // ── Handle positions ──────────────────────────────────────────────────

        public override Vector3 HandleWorldPosition(int i) => i switch
        {
            0 => Center,
            1 => Center + Axis *  HalfHeight,
            2 => Center - Axis *  HalfHeight,
            3 => Center + LocalX *  Radius,
            4 => Center + LocalX * -Radius,
            5 => Center + LocalZ *  Radius,
            6 => Center + LocalZ * -Radius,
            _ => Center
        };

        // ── Handle drag ───────────────────────────────────────────────────────

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;
            _editStartRotation   = Rotation;

            if (handle >= 3)
            {
                var (cx, cy, _) = cam.WorldToScreen(Center);
                var (px, py, _) = cam.WorldToScreen(HandleWorldPosition(handle));
                float dx = px - cx, dy = py - cy;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                _radialScreenDir = len > 0.5f
                    ? new Vector2(dx / len, dy / len)
                    : new Vector2(1f, 0f);
            }
        }

        public override void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle < 0) return;

            switch (_activeHandle)
            {
                case 0:
                {
                    Vector3 s = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 c = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    Center = _editStartCenter + (c - s);
                    break;
                }
                case 1:
                case 2:
                {
                    Vector3 s    = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 c    = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    float   proj = Vector3.Dot(c - s, Axis);
                    float   sign = _activeHandle == 1 ? 1f : -1f;
                    HalfHeight   = MathF.Max(_editStartHalfHeight + sign * proj, 0.01f);
                    break;
                }
                default:
                {
                    float proj = (mx - _editStartX) * _radialScreenDir.X
                               + (my - _editStartY) * _radialScreenDir.Y;
                    Radius = MathF.Max(_editStartRadius * (1f + proj * MouseDragSensitivity), 0.01f);
                    break;
                }
            }
        }

        // ── Keyboard editing ──────────────────────────────────────────────────

        public override void BeginRotate(int mx, int my, OrbitCamera camera)
        {
            if (!IsEditing) return;
            _kbAction          = EditAction.Rotate;
            _editStartX        = mx;
            _editStartRotation = Rotation;
        }

        protected override void OnBeginScaleExtra(int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;
        }

        protected override void UpdateEditShape(EditAction action, int mx, int my, OrbitCamera cam)
        {
            int dx = mx - _editStartX;
            switch (action)
            {
                case EditAction.Scale:
                {
                    float f = 1f + dx * MouseDragSensitivity;
                    if      (_kbAxis == 1)            HalfHeight = MathF.Max(_editStartHalfHeight * f, 0.01f);
                    else if (_kbAxis == 0 || _kbAxis == 2) Radius = MathF.Max(_editStartRadius * f, 0.01f);
                    else { Radius = MathF.Max(_editStartRadius * f, 0.01f); HalfHeight = MathF.Max(_editStartHalfHeight * f, 0.01f); }
                    break;
                }
                case EditAction.Rotate:
                {
                    float   angle = dx * 0.5f * MathF.PI / 180f;
                    Vector3 ax    = _kbAxis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
                    Rotation = _editStartRotation * Quaternion.FromAxisAngle(ax, angle);
                    Rotation.Normalize();
                    break;
                }
            }
        }

        public override void AdjustScale(float delta)
        {
            if (!IsEditing) return;
            float fac = 1f + delta * ScrollScaleFactor;
            Radius     = MathF.Max(Radius     * fac, 0.01f);
            HalfHeight = MathF.Max(HalfHeight * fac, 0.01f);
        }

        public override void Cancel()
        {
            base.Cancel();
            Radius     = 0f;
            HalfHeight = 0f;
            Rotation   = Quaternion.FromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
            _extruding = false;
        }

        // ── Resolution ────────────────────────────────────────────────────────
        // Fast path: no per-point allocation, only dot products on precomputed axis vectors.

        public override HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            if (Radius < 1e-4f || HalfHeight < 1e-4f) return new HashSet<int>();

            float   r2 = Radius * Radius;
            float   hh = HalfHeight;
            float   cx = Center.X, cy = Center.Y, cz = Center.Z;
            Vector3 ax = Axis; // precompute once

            var list = new List<int>(capacity: 256);
            for (int i = 0; i < points.Length; i++)
            {
                float dx = points[i].X - cx;
                float dy = points[i].Y - cy;
                float dz = points[i].Z - cz;

                // Height along axis
                float h = dx * ax.X + dy * ax.Y + dz * ax.Z;
                if (MathF.Abs(h) > hh) continue;

                // Radial distance squared = |d|² - h²
                float radial2 = dx * dx + dy * dy + dz * dz - h * h;
                if (radial2 <= r2) list.Add(i);
            }
            return new HashSet<int>(list);
        }
    }
}
