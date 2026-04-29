using System;
using System.Collections.Generic;
using CloudScope.Library;

namespace CloudScope.Loading
{
    public static class PointCloudLoader
    {
        private const int ColorSampleSize = 1000;

        public static LoadedPointCloud Load(LasReader reader, long maxPoints, IProgress<int>? progress = null)
        {
            var hdr = reader.Header;
            bool hasColor = hdr.PointDataFormatId is 2 or 3 or 5 or 7 or 8 or 10;
            long total = maxPoints > 0 ? Math.Min(maxPoints, reader.PointCount) : reader.PointCount;
            var points = new PointData[total];

            double cx = (hdr.MinX + hdr.MaxX) * 0.5;
            double cy = (hdr.MinY + hdr.MaxY) * 0.5;
            double cz = (hdr.MinZ + hdr.MaxZ) * 0.5;
            double spanZ = hdr.MaxZ - hdr.MinZ;

            float colorScale = 1.0f / 65535.0f;
            var colorSample = hasColor
                ? new List<LasPoint>((int)Math.Min(ColorSampleSize, total))
                : null;

            long loaded = 0;
            int lastPct = -1;

            foreach (var pt in reader.GetPoints())
            {
                if (loaded >= total) break;

                if (colorSample != null && colorSample.Count < ColorSampleSize)
                {
                    colorSample.Add(pt);
                    loaded++;

                    if (colorSample.Count == ColorSampleSize || loaded == total)
                    {
                        colorScale = DetectColorScale(colorSample);
                        for (int i = 0; i < colorSample.Count; i++)
                            WritePoint(ref points[i], colorSample[i], cx, cy, cz, spanZ, hasColor, colorScale, hdr.MinZ);
                        ReportProgress(loaded, total, progress, ref lastPct);
                    }
                    continue;
                }

                WritePoint(ref points[loaded], pt, cx, cy, cz, spanZ, hasColor, colorScale, hdr.MinZ);
                loaded++;
                ReportProgress(loaded, total, progress, ref lastPct);
            }

            float rangeX = (float)(hdr.MaxX - hdr.MinX) * 0.5f;
            float rangeY = (float)(hdr.MaxY - hdr.MinY) * 0.5f;
            float rangeZ = (float)(hdr.MaxZ - hdr.MinZ) * 0.5f;
            float radius = MathF.Sqrt(rangeX * rangeX + rangeY * rangeY + rangeZ * rangeZ);

            return new LoadedPointCloud(points, loaded, radius, hasColor, colorScale, cx, cy, cz);
        }

        public static void ShuffleForProgressiveLod(PointData[] points, long count, int seed = 42)
        {
            var rng = new Random(seed);
            for (long i = count - 1; i > 0; i--)
            {
                long j = (long)(rng.NextDouble() * (i + 1));
                (points[i], points[j]) = (points[j], points[i]);
            }
        }

        private static float DetectColorScale(List<LasPoint> sample)
        {
            ushort max = 0;
            foreach (var pt in sample)
            {
                if (pt.R > max) max = pt.R;
                if (pt.G > max) max = pt.G;
                if (pt.B > max) max = pt.B;
            }

            return max > 0 && max <= 255
                ? 1.0f / 255.0f
                : 1.0f / 65535.0f;
        }

        private static void WritePoint(
            ref PointData point,
            LasPoint source,
            double centerX,
            double centerY,
            double centerZ,
            double spanZ,
            bool hasColor,
            float colorScale,
            double minZ)
        {
            point.X = (float)(source.X - centerX);
            point.Y = (float)(source.Y - centerY);
            point.Z = (float)(source.Z - centerZ);

            if (hasColor)
            {
                point.R = source.R * colorScale;
                point.G = source.G * colorScale;
                point.B = source.B * colorScale;
                return;
            }

            float t = spanZ > 0 ? (float)((source.Z - minZ) / spanZ) : 0.5f;
            t = Math.Clamp(t, 0f, 1f);
            point.R = t;
            point.G = 1f - MathF.Abs(2f * t - 1f);
            point.B = 1f - t;
        }

        private static void ReportProgress(long loaded, long total, IProgress<int>? progress, ref int lastPct)
        {
            if (total <= 0) return;

            int pct = (int)(loaded * 100L / total);
            if (pct / 5 == lastPct / 5) return;

            lastPct = pct;
            progress?.Report(pct);
        }
    }
}
