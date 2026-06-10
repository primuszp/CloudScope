using System;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public readonly record struct GripArrow3D(Vector3 Start, Vector3 Tip);

    public static class GripArrowSupport
    {
        public const float HitRadiusPixels   = 14f;
        public const float ArrowHeightPixels = 80f;  // total arrow screen length (shaft + cone)
        public const float ConeHeightPixels  = 22f;  // cone tip screen height
        public const float ConeRadiusPixels  =  9f;  // cone base screen radius

        // ── Adaptive sizing (object-aware, pixel-clamped) ─────────────────────
        // The arrow length tracks the object's on-screen size, clamped to a pixel
        // range. Large objects → MaxArrowPixels (stable feel, no jitter while
        // resizing); small / zoomed-out objects → shrink proportionally so the
        // arrows never dwarf the shape; never below MinArrowPixels (stays grabbable).
        public const float MinArrowPixels   = 26f;
        public const float MaxArrowPixels   = 86f;
        public const float ObjectFillFactor = 0.75f; // arrow ≈ 75% of object screen radius

        // Cone is a fixed fraction of the arrow length, so it always looks like an arrow.
        public const float ConeToArrowRatio   = ConeHeightPixels / ArrowHeightPixels; // 0.275
        public const float ConeRadiusToArrow  = ConeRadiusPixels / ArrowHeightPixels; // 0.1125

        public static GripArrow3D Create(GripDescriptor grip, float length) =>
            new(grip.Position, grip.Position + grip.Direction * MathF.Max(length, 0.01f));

        /// <summary>
        /// World-space arrow length that looks proportional at every scale:
        /// length(px) = clamp(objectScreenRadius * ObjectFillFactor, Min, Max) * emphasis,
        /// converted back to world units at the grip's depth.
        /// </summary>
        public static float AdaptiveWorldLength(
            float objectWorldRadius, Vector3 gripPos, OrbitCamera cam, float emphasis = 1f)
        {
            float wpp = WorldUnitsPerPixel(gripPos, cam);
            if (wpp < 1e-9f)
                return MathF.Max(objectWorldRadius * ObjectFillFactor * emphasis, 0.01f);

            float objectScreenRadius = objectWorldRadius / wpp;
            float px = Math.Clamp(objectScreenRadius * ObjectFillFactor, MinArrowPixels, MaxArrowPixels);
            return px * emphasis * wpp;
        }

        /// <summary>
        /// Creates a screen-size-stable arrow: the shaft is always
        /// <paramref name="totalScreenPixels"/> pixels long regardless of zoom.
        /// </summary>
        public static GripArrow3D CreateScreenSized(
            GripDescriptor grip,
            OrbitCamera    cam,
            float          totalScreenPixels = ArrowHeightPixels)
        {
            float len = WorldUnitsPerPixel(grip.Position, cam) * totalScreenPixels;
            return Create(grip, MathF.Max(len, 0.001f));
        }

        /// <summary>
        /// Returns how many world-space units correspond to one screen pixel
        /// at <paramref name="worldPos"/> depth. Delegates to the camera's analytic Hilton math.
        /// </summary>
        public static float WorldUnitsPerPixel(Vector3 worldPos, OrbitCamera cam)
            => cam.WorldUnitsPerPixel(worldPos);

        public static float ScreenHitDistance(
            GripArrow3D arrow,
            OrbitCamera camera,
            int mouseX,
            int mouseY)
        {
            var (startX, startY, startBehind) = camera.WorldToScreen(arrow.Start);
            var (tipX, tipY, tipBehind) = camera.WorldToScreen(arrow.Tip);
            if (startBehind && tipBehind)
                return float.MaxValue;

            float shaft = GripInteractionMath.SegmentDistance(mouseX, mouseY, startX, startY, tipX, tipY);
            float start = PointDistance(mouseX, mouseY, startX, startY);
            float tip = PointDistance(mouseX, mouseY, tipX, tipY);
            return MathF.Min(shaft, MathF.Min(start, tip));
        }

        private static float PointDistance(float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
