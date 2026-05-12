using System;

namespace CloudScope.Rendering
{
    internal static class PointDrawBudget
    {
        private const float OverviewFraction = 0.04f;
        private const float MinimumFraction = 0.005f;
        private const int MinimumVisiblePoints = 100_000;

        public static int Compute(int pointCount, double halfViewSize, float cloudRadius, int? hardCap = null)
        {
            if (pointCount <= 0)
                return 0;

            float zoomRatio = (float)(cloudRadius / Math.Max(halfViewSize, cloudRadius * 0.001));
            float drawFraction = Math.Clamp(OverviewFraction * zoomRatio * zoomRatio, MinimumFraction, 1f);
            int drawCount = Math.Max((int)(pointCount * drawFraction), Math.Min(MinimumVisiblePoints, pointCount));

            if (hardCap is > 0)
                drawCount = Math.Min(drawCount, hardCap.Value);

            return Math.Min(drawCount, pointCount);
        }
    }
}
