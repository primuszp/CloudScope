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

        public static GripArrow3D Create(GripDescriptor grip, float length) =>
            new(grip.Position, grip.Position + grip.Direction * MathF.Max(length, 0.01f));

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
        /// at <paramref name="worldPos"/> depth.
        /// </summary>
        public static float WorldUnitsPerPixel(Vector3 worldPos, OrbitCamera cam)
        {
            var (sx0, sy0, b0) = cam.WorldToScreen(worldPos);
            if (b0) return 0.001f;

            const float step = 1f;
            var (sx1, sy1, b1) = cam.WorldToScreen(worldPos + new Vector3(step, 0f, 0f));
            if (!b1)
            {
                float d = MathF.Sqrt((sx1 - sx0) * (sx1 - sx0) + (sy1 - sy0) * (sy1 - sy0));
                if (d > 0.5f) return step / d;
            }
            (sx1, sy1, b1) = cam.WorldToScreen(worldPos + new Vector3(0f, step, 0f));
            if (!b1)
            {
                float d = MathF.Sqrt((sx1 - sx0) * (sx1 - sx0) + (sy1 - sy0) * (sy1 - sy0));
                if (d > 0.5f) return step / d;
            }
            return 0.001f;
        }

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
