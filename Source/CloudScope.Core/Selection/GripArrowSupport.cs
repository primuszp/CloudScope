using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public readonly record struct GripArrow3D(Vector3 Start, Vector3 Tip);

    public static class GripArrowSupport
    {
        public const float HitRadiusPixels = 14f;

        public static GripArrow3D Create(GripDescriptor grip, float length) =>
            new(grip.Position, grip.Position + grip.Direction * MathF.Max(length, 0.01f));

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
