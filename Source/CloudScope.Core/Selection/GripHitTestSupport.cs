using System;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    internal static class GripHitTestSupport
    {
        public static float PointDistance(
            OrbitCamera camera,
            Vector3 worldPosition,
            int mouseX,
            int mouseY)
        {
            var (screenX, screenY, behind) = camera.WorldToScreen(worldPosition);
            if (behind)
                return float.MaxValue;

            float dx = screenX - mouseX;
            float dy = screenY - mouseY;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
