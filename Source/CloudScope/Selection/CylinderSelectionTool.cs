using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// World-space cylinder selection with two-phase workflow.
    /// Cylinder axis is always world-Y (up). Placement is identical to sphere:
    /// click to set center, drag to set radius. Release → Editing (default height = 2×radius).
    ///
    /// Handle indices:
    ///   0 = center         (move)
    ///   1 = top cap        (Center + Y * HalfHeight)
    ///   2 = bottom cap     (Center - Y * HalfHeight)
    ///   3 = +X radial,  4 = -X radial
    ///   5 = +Z radial,  6 = -Z radial
    ///
    /// Handles 1/2 drag: adjust height.
    /// Handles 3-6 drag: adjust radius (projected onto screen dir).
    /// G/S keyboard + scroll also work as on sphere.
    /// </summary>
    public sealed class CylinderSelectionTool : SelectionToolBase
    {
        public override SelectionToolType ToolType    => SelectionToolType.Cylinder;
        public override int               HandleCount => 7;

        public override bool HasVolume =>
            (IsActive || IsEditing) && Radius > 1e-4f && HalfHeight > 1e-4f;

        public float Radius     { get; set; }
        public float HalfHeight { get; set; }

        // ── Cylinder-specific drag state ──────────────────────────────────────
        private float   _editStartRadius;
        private float   _editStartHalfHeight;
        private Vector2 _radialScreenDir;   // screen dir from center toward radial handle

        // ── Placement ─────────────────────────────────────────────────────────

        public override void OnMouseDown(int mx, int my, OrbitCamera camera)
        {
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Center     = worldPt;
            Radius     = 0f;
            HalfHeight = 0f;
            Phase      = ToolPhase.Drawing;
        }

        public override void OnMouseMove(int mx, int my, OrbitCamera camera)
        {
            if (!IsActive) return;
            var (worldPt, _) = camera.ScreenToWorldPoint(mx, my, 21);
            Radius     = (worldPt - Center).Length;
            HalfHeight = MathF.Max(Radius, 0.01f);   // live preview: height tracks radius
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
            1 => Center + new Vector3(0f,  HalfHeight, 0f),
            2 => Center + new Vector3(0f, -HalfHeight, 0f),
            3 => Center + new Vector3( Radius, 0f, 0f),
            4 => Center + new Vector3(-Radius, 0f, 0f),
            5 => Center + new Vector3(0f, 0f,  Radius),
            6 => Center + new Vector3(0f, 0f, -Radius),
            _ => Center
        };

        // ── Handle drag ───────────────────────────────────────────────────────

        protected override void OnBeginHandleDragExtra(int handle, int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;

            if (handle >= 3)
            {
                // Screen-space direction from center toward this radial handle
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
                case 1: // Top cap — adjust height upward
                case 2: // Bottom cap — adjust height downward
                {
                    // Project mouse delta onto screen-Y axis (positive = up on screen)
                    float dy    = -( my - _editStartY);   // invert: screen Y grows downward
                    float sign  = _activeHandle == 1 ? 1f : -1f;
                    float delta = sign * dy * MouseDragSensitivity * _editStartHalfHeight * 4f;
                    HalfHeight  = MathF.Max(_editStartHalfHeight + delta, 0.01f);
                    break;
                }
                default: // Radial handle — adjust radius
                {
                    float proj   = (mx - _editStartX) * _radialScreenDir.X
                                 + (my - _editStartY) * _radialScreenDir.Y;
                    float factor = 1f + proj * MouseDragSensitivity;
                    Radius       = MathF.Max(_editStartRadius * factor, 0.01f);
                    break;
                }
            }
        }

        // ── Keyboard scale ────────────────────────────────────────────────────

        protected override void OnBeginScaleExtra(int mx, int my, OrbitCamera cam)
        {
            _editStartRadius     = Radius;
            _editStartHalfHeight = HalfHeight;
        }

        protected override void UpdateEditShape(EditAction action, int mx, int my, OrbitCamera cam)
        {
            if (action == EditAction.Scale)
            {
                float factor = 1f + (mx - _editStartX) * MouseDragSensitivity;
                if (_kbAxis == 1)
                {
                    // Y-axis constraint: scale height only
                    HalfHeight = MathF.Max(_editStartHalfHeight * factor, 0.01f);
                }
                else if (_kbAxis == 0 || _kbAxis == 2)
                {
                    // X or Z constraint: scale radius only
                    Radius = MathF.Max(_editStartRadius * factor, 0.01f);
                }
                else
                {
                    // Uniform: scale both
                    Radius     = MathF.Max(_editStartRadius     * factor, 0.01f);
                    HalfHeight = MathF.Max(_editStartHalfHeight * factor, 0.01f);
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
        }

        // ── Resolution ────────────────────────────────────────────────────────

        public override HashSet<int> ResolveSelection(PointData[] points, OrbitCamera camera, int vpW, int vpH)
        {
            if (Radius < 1e-4f || HalfHeight < 1e-4f) return new HashSet<int>();

            float r2  = Radius * Radius;
            float hh  = HalfHeight;
            float cx  = Center.X, cy = Center.Y, cz = Center.Z;

            var list = new List<int>(capacity: 256);
            for (int i = 0; i < points.Length; i++)
            {
                float dy = points[i].Y - cy;
                if (MathF.Abs(dy) > hh) continue;          // height cull

                float dx = points[i].X - cx;
                float dz = points[i].Z - cz;
                if (dx * dx + dz * dz <= r2) list.Add(i);  // radial cull
            }
            return new HashSet<int>(list);
        }
    }
}
