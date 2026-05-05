using System;
using CloudScope.Library;

namespace CloudScope.Loading
{
    public static class PointCloudLoader
    {
        private const int ColorSampleSize = 1000;
        private const int BatchSize = 1 << 17;

        public static LoadedPointCloud Load(LasReader reader, long maxPoints, IProgress<int>? progress = null)
        {
            var hdr = reader.Header;
            bool hasColor = hdr.PointDataFormatId is 2 or 3 or 5 or 7 or 8 or 10;
            long requested = maxPoints > 0 ? Math.Min(maxPoints, reader.PointCount) : reader.PointCount;
            if (requested > int.MaxValue)
                throw new InvalidOperationException($"Point limit {requested:N0} exceeds the in-memory viewer limit of {int.MaxValue:N0} points.");

            int total = (int)requested;
            var points = new PointData[total];

            double cx = (hdr.MinX + hdr.MaxX) * 0.5;
            double cy = (hdr.MinY + hdr.MaxY) * 0.5;
            double cz = (hdr.MinZ + hdr.MaxZ) * 0.5;
            double spanZ = hdr.MaxZ - hdr.MinZ;

            float colorScale = 1.0f / 65535.0f;
            var batch = new LasPoint[(int)Math.Min(BatchSize, Math.Max(total, 1))];
            var rawBatch = new byte[batch.Length * hdr.PointDataRecordLength];

            int loaded = 0;
            int lastPct = -1;

            while (loaded < total)
            {
                int read = reader.ReadBatch(loaded, batch, Math.Min(batch.Length, total - loaded), rawBatch);
                if (read == 0) break;

                if (hasColor && loaded == 0)
                    colorScale = DetectColorScale(batch, Math.Min(read, ColorSampleSize));

                for (int i = 0; i < read; i++)
                    WritePoint(ref points[loaded + i], batch[i], cx, cy, cz, spanZ, hasColor, colorScale, hdr.MinZ);

                loaded += read;
                ReportProgress(loaded, total, progress, ref lastPct);
            }

            float rangeX = (float)(hdr.MaxX - hdr.MinX) * 0.5f;
            float rangeY = (float)(hdr.MaxY - hdr.MinY) * 0.5f;
            float rangeZ = (float)(hdr.MaxZ - hdr.MinZ) * 0.5f;
            float radius = MathF.Sqrt(rangeX * rangeX + rangeY * rangeY + rangeZ * rangeZ);

            return new LoadedPointCloud(points, loaded, radius, hasColor, colorScale, cx, cy, cz);
        }

        public static void ShuffleForProgressiveSubsample(PointData[] points, long count, int seed = 42)
        {
            var rng = new Random(seed);
            for (long i = count - 1; i > 0; i--)
            {
                long j = (long)(rng.NextDouble() * (i + 1));
                (points[i], points[j]) = (points[j], points[i]);
            }
        }

        private static float DetectColorScale(LasPoint[] sample, int count)
        {
            ushort max = 0;
            for (int i = 0; i < count; i++)
            {
                var pt = sample[i];
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
