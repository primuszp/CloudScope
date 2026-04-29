using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space cylinder selection — two-phase workflow matching the box tool.
    ///
    /// Phase 1 – Drawing:
    ///   Left-drag on screen draws a circle (center = drag midpoint, radius = half drag distance).
    ///   Mouse-up triggers FinalizeDiskFromScreen (called by viewer), which places a flat disk
    ///   in world space facing the camera, then auto-starts extruding the top cap.
    ///
    /// Phase 1b – Extruding (internal, Phase still == Editing):
    ///   Mouse move (no button) adjusts HalfHeight live — exactly like box's orange extrude arrow.
    ///   Left-click confirms height and fully enters Editing.
    ///
    /// Phase 2 – Editing:
    ///   7 handles: center(0), top(1), bottom(2), ±local-X(3/4), ±local-Z(5/6).
    ///   G/S/R keyboard (with X/Y/Z axis constraints), scroll = fine-tune.
    /// </summary>
    public sealed class CylinderSelectionTool : SelectionToolBase
    {
        public override SelectionToolType ToolType    => SelectionToolType.Cylinder;
        public override int               HandleCount => 7;

        public override bool HasVolume =>
            IsEditing && !_extruding && Radius > 1e-4f && HalfHeight > 1e-4f;

        public float      Radius     { get; set; }
        public float      HalfHeight { get; set; }
        public Quaternion Rotation   = Quaternion.Identity;

        // Drawing screen coords (like BoxSelectionTool)
        public int StartX, StartY, EndX, EndY;

        // True while the user is still dragging out the height after disk placement
        public bool IsExtruding => _extruding;
        private bool _extruding;

        // World-space extrude axis (camera-forward at placement time, = cylinder axis)
        private Vector3 _extrudeAxis;
        private Vector3 _extrudeOrigin; // world point under cursor when extrude began
        private float   _extrudeViewZ;

        // Local axes derived from rotation
        public Vector3 Axis   => Vector3.Transform(Vector3.UnitY, Rotation);
        private Vector3 LocalX => Vector3.Transform(Vector3.UnitX, Rotation);
        private Vector3 LocalZ => Vector3.Transform(Vector3.UnitZ, Rotation);

        // ── Cylinder-specific drag / edit state ───────────────────────────────
        private float      _editStartRadius;
        private float      _editStartHalfHeight;
        private Quaternion _editStartRotation;
        private Vector2    _radialScreenDir;

        // ── Phase 1: Drawing (screen-drag) ────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            StartX = EndX = mx;
            StartY = EndY = my;
            Radius     = 0f;
            HalfHeight = 0f;
            Rotation   = Quaternion.Identity;
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
            // Small drag → cancel; otherwise viewer calls FinalizeDiskFromScreen
            if (Math.Abs(EndX - StartX) < 5 && Math.Abs(EndY - StartY) < 5)
                Phase = ToolPhase.Idle;
        }

        /// <summary>
        /// Build the initial flat disk from the drawn screen circle, then begin extruding.
        /// Called by the viewer immediately after OnMouseUp while Phase == Drawing.
        /// </summary>
        public void FinalizeDiskFromScreen(OrbitCamera cam)
        {
            // Bounding box center of the drawn rectangle
            int x0 = Math.Min(StartX, EndX), x1 = Math.Max(StartX, EndX);
            int y0 = Math.Min(StartY, EndY), y1 = Math.Max(StartY, EndY);
            int cxs = (x0 + x1) / 2, cys = (y0 + y1) / 2;

            var (centerWorld, _) = cam.ScreenToWorldPoint(cxs, cys, 11);
            float refViewZ = cam.WorldToViewZ(centerWorld);
            Center = cam.ScreenToWorldAtDepth(cxs, cys, refViewZ);

            // Radius = half the screen diagonal mapped to world space
            Vector3 corner  = cam.ScreenToWorldAtDepth(x1, cys, refViewZ);
            Radius = (corner - Center).Length;
            if (Radius < 0.001f) { Phase = ToolPhase.Idle; return; }

            // Cylinder axis = camera forward at draw time (same direction as box extrude)
            Vector3 camFwd = cam.CameraForward.Normalized();

            // Build rotation: local Y = camFwd
            Vector3 up     = MathF.Abs(Vector3.Dot(camFwd, Vector3.UnitZ)) < 0.99f
                             ? Vector3.UnitZ : Vector3.UnitX;
            Vector3 right  = Vector3.Cross(camFwd, up).Normalized();
            Vector3 fwdUp  = Vector3.Cross(right, camFwd).Normalized();
            // local X=right, local Y=camFwd, local Z=fwdUp — feed transpose to OpenTK
            Matrix3 rotMat = new Matrix3(right, camFwd, fwdUp);
            Rotation = Quaternion.FromMatrix(Matrix3.Transpose(rotMat));
            Rotation.Normalize();

            HalfHeight    = 0.001f;                     // start as a thin disk
            Phase         = ToolPhase.Editing;
            _extruding    = true;
            _extrudeAxis  = Axis;                       // = camFwd
            _extrudeOrigin = Center;
            _extrudeViewZ  = cam.WorldToViewZ(Center);
        }

        /// <summary>
        /// Called every frame during extrude phase (mouse move without button).
        /// Updates HalfHeight based on mouse position along the extrude axis.
        /// </summary>
        public void UpdateExtrude(int mx, int my, OrbitCamera cam)
        {
            if (!_extruding) return;
            Vector3 curWorld = cam.ScreenToWorldAtDepth(mx, my, _extrudeViewZ);
            float   proj     = Vector3.Dot(curWorld - _extrudeOrigin, _extrudeAxis);
            HalfHeight = MathF.Max(MathF.Abs(proj), 0.001f);
            // Shift center along axis so disk stays at origin end
            Center = _extrudeOrigin + _extrudeAxis * (proj * 0.5f);
        }

        /// <summary>Confirm extrude height on left-click; tool stays in Editing.</summary>
        public void ConfirmExtrude()
        {
            _extruding = false;
        }

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
                _radialScreenDir = len > 0.5f ? new Vector2(dx / len, dy / len) : new Vector2(1f, 0f);
            }
        }

        public override void UpdateHandleDrag(int mx, int my, OrbitCamera cam)
        {
            if (_activeHandle < 0) return;

            switch (_activeHandle)
            {
                case 0: // Center — depth-correct world drag
                {
                    Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 curWorld   = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    Center = _editStartCenter + (curWorld - startWorld);
                    break;
                }
                case 1: // Top cap
                case 2: // Bottom cap — move along cylinder axis
                {
                    Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 curWorld   = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    float   proj       = Vector3.Dot(curWorld - startWorld, Axis);
                    float   sign       = _activeHandle == 1 ? 1f : -1f;
                    HalfHeight         = MathF.Max(_editStartHalfHeight + sign * proj, 0.01f);
                    break;
                }
                default: // Radial handle
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
                    float factor = 1f + dx * MouseDragSensitivity;
                    if (_kbAxis == 1)
                        HalfHeight = MathF.Max(_editStartHalfHeight * factor, 0.01f);
                    else if (_kbAxis == 0 || _kbAxis == 2)
                        Radius = MathF.Max(_editStartRadius * factor, 0.01f);
                    else
                    {
                        Radius     = MathF.Max(_editStartRadius     * factor, 0.01f);
                        HalfHeight = MathF.Max(_editStartHalfHeight * factor, 0.01f);
                    }
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
            float fac  = 1f + delta * ScrollScaleFactor;
            Radius     = MathF.Max(Radius     * fac, 0.01f);
            HalfHeight = MathF.Max(HalfHeight * fac, 0.01f);
        }

        public override void Cancel()
        {
            base.Cancel();
            Radius     = 0f;
            HalfHeight = 0f;
            Rotation   = Quaternion.Identity;
            _extruding = false;
        }

        // ── Resolution ────────────────────────────────────────────────────────

        public override HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            if (Radius < 1e-4f || HalfHeight < 1e-4f) return new HashSet<int>();

            float      r2  = Radius * Radius;
            float      hh  = HalfHeight;
            Quaternion inv = Quaternion.Invert(Rotation);

            var list = new List<int>(capacity: 256);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 local = Vector3.Transform(
                    new Vector3(points[i].X - Center.X, points[i].Y - Center.Y, points[i].Z - Center.Z),
                    inv);
                if (MathF.Abs(local.Y) > hh)                   continue;
                if (local.X * local.X + local.Z * local.Z > r2) continue;
                list.Add(i);
            }
            return new HashSet<int>(list);
        }
    }
}
