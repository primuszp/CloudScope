using System;

namespace CloudScope.Rendering
{
    internal static class PointDrawBudget
    {
        private const float OverviewFraction = 0.04f;
        private const float MinimumFraction = 0.005f;
        private const int MinimumVisiblePoints = 100_000;
        private const float FullDensityZoomRatio = 4f;

        public static int Compute(int pointCount, double halfViewSize, float cloudRadius, int? hardCap = null)
        {
            if (pointCount <= 0)
                return 0;

            if (!double.IsFinite(halfViewSize) || halfViewSize <= 0 || !float.IsFinite(cloudRadius) || cloudRadius <= 0)
                return hardCap is > 0 ? Math.Min(pointCount, hardCap.Value) : pointCount;

            float zoomRatio = (float)(cloudRadius / Math.Max(halfViewSize, cloudRadius * 0.001));
            float normalizedZoom = zoomRatio / FullDensityZoomRatio;
            float drawFraction = Math.Clamp(
                MinimumFraction + (1f - MinimumFraction) * normalizedZoom * normalizedZoom,
                MinimumFraction,
                1f);
            int drawCount = Math.Max((int)(pointCount * drawFraction), Math.Min(MinimumVisiblePoints, pointCount));

            if (hardCap is > 0)
                drawCount = Math.Min(drawCount, hardCap.Value);

            return Math.Min(drawCount, pointCount);
        }
    }
}
