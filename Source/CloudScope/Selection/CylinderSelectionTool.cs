using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space cylinder selection with two-phase workflow.
    /// The cylinder axis is Rotation * Y. Placement: click center, drag radius.
    /// Release → Editing (default height = 2×radius, Rotation = Identity).
    ///
    /// Handle indices:
    ///   0 = center         (move)
    ///   1 = top cap        (Center + axis * HalfHeight)
    ///   2 = bottom cap     (Center - axis * HalfHeight)
    ///   3 = +X radial,  4 = -X radial  (in local space)
    ///   5 = +Z radial,  6 = -Z radial  (in local space)
    ///
    /// Handles 1/2: adjust HalfHeight.
    /// Handles 3-6: adjust Radius.
    /// G = Grab, S = Scale, R = Rotate (+ X/Y/Z axis constraint), scroll = fine-tune.
    /// </summary>
    public sealed class CylinderSelectionTool : SelectionToolBase
    {
        public override SelectionToolType ToolType    => SelectionToolType.Cylinder;
        public override int               HandleCount => 7;

        public override bool HasVolume =>
            (IsActive || IsEditing) && Radius > 1e-4f && HalfHeight > 1e-4f;

        public float      Radius     { get; set; }
        public float      HalfHeight { get; set; }
        public Quaternion Rotation   = Quaternion.Identity;

        // Local axes derived from rotation
        private Vector3 Axis   => Vector3.Transform(Vector3.UnitY, Rotation); // cylinder axis
        private Vector3 LocalX => Vector3.Transform(Vector3.UnitX, Rotation);
        private Vector3 LocalZ => Vector3.Transform(Vector3.UnitZ, Rotation);

        // ── Cylinder-specific drag / edit state ───────────────────────────────
        private float     _editStartRadius;
        private float     _editStartHalfHeight;
        private Quaternion _editStartRotation;
        private Vector2   _radialScreenDir;

        // ── Placement ─────────────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Center     = worldPt;
            Radius     = 0f;
            HalfHeight = 0f;
            Rotation   = Quaternion.Identity;
            Phase      = ToolPhase.Drawing;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Radius     = (worldPt - Center).Length;
            HalfHeight = MathF.Max(Radius, 0.01f);
        }

        public override void OnMouseUp(int mx, int my)
        {
            if (!IsActive) return;
            Phase = Radius > 0.01f ? ToolPhase.Editing : ToolPhase.Idle;
        }

        // ── Handle positions ──────────────────────────────────────────────────

        public override Vector3 HandleWorldPosition(int i) => i switch
        {
            0 => Center,
            1 => Center + Axis * HalfHeight,
            2 => Center - Axis * HalfHeight,
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
                case 1: // Top cap — move along +axis
                case 2: // Bottom cap — move along -axis
                {
                    // Project cap handle world movement onto the cylinder axis
                    Vector3 startWorld = cam.ScreenToWorldAtDepth(_editStartX, _editStartY, _editViewZ);
                    Vector3 curWorld   = cam.ScreenToWorldAtDepth(mx, my, _editViewZ);
                    float   proj       = Vector3.Dot(curWorld - startWorld, Axis);
                    float   sign       = _activeHandle == 1 ? 1f : -1f;
                    HalfHeight         = MathF.Max(_editStartHalfHeight + sign * proj, 0.01f);
                    break;
                }
                default: // Radial handle — adjust radius via screen projection
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
        }

        // ── Resolution ────────────────────────────────────────────────────────

        public override HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            if (Radius < 1e-4f || HalfHeight < 1e-4f) return new HashSet<int>();

            float r2      = Radius * Radius;
            float hh      = HalfHeight;
            Quaternion inv = Quaternion.Invert(Rotation);

            var list = new List<int>(capacity: 256);
            for (int i = 0; i < points.Length; i++)
            {
                // Transform point into local cylinder space
                Vector3 local = Vector3.Transform(
                    new Vector3(points[i].X - Center.X, points[i].Y - Center.Y, points[i].Z - Center.Z),
                    inv);

                if (MathF.Abs(local.Y) > hh)      continue; // height cull
                if (local.X * local.X + local.Z * local.Z > r2) continue; // radial cull
                list.Add(i);
            }
            return new HashSet<int>(list);
        }
    }
}
