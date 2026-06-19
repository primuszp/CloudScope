using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// Fits a tight primitive (OBB box, bounding sphere, upright cylinder) to a set of
    /// world-space points. The box uses a 2-D PCA in XY for the yaw, giving a minimum-area
    /// footprint; sphere/cylinder are upright (axis = world Z) to match the tools.
    /// </summary>
    public static class ShapeFitter
    {
        public static void FitBox(IReadOnlyList<Vector3> points, out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
        {
            float angle = PrincipalYaw(points);
            Vector3 axisX = new(MathF.Cos(angle), MathF.Sin(angle), 0f);
            Vector3 axisY = new(-MathF.Sin(angle), MathF.Cos(angle), 0f);
            Vector3 axisZ = Vector3.UnitZ;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 p in points)
            {
                float px = Vector3.Dot(p, axisX);
                float py = Vector3.Dot(p, axisY);
                float pz = p.Z;
                if (px < minX) minX = px; if (px > maxX) maxX = px;
                if (py < minY) minY = py; if (py > maxY) maxY = py;
                if (pz < minZ) minZ = pz; if (pz > maxZ) maxZ = pz;
            }

            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            center = axisX * cx + axisY * cy + axisZ * cz;
            halfExtents = new Vector3(
                MathF.Max((maxX - minX) * 0.5f, 0.01f),
                MathF.Max((maxY - minY) * 0.5f, 0.01f),
                MathF.Max((maxZ - minZ) * 0.5f, 0.01f));

            // Same convention as BoxSelectionTool: CreateFromQuaternion(rotation) maps world→local.
            var rotMat = new Matrix3(axisX, axisY, axisZ);
            rotation = Quaternion.FromMatrix(Matrix3.Transpose(rotMat));
            rotation.Normalize();
        }

        public static void FitSphere(IReadOnlyList<Vector3> points, out Vector3 center, out float radius)
        {
            center = Centroid(points);
            float maxSq = 0f;
            foreach (Vector3 p in points)
            {
                float d = (p - center).LengthSquared;
                if (d > maxSq) maxSq = d;
            }
            radius = MathF.Max(MathF.Sqrt(maxSq), 0.01f);
        }

        public static void FitCylinder(IReadOnlyList<Vector3> points, out Vector3 center, out float radius, out float halfHeight)
        {
            Vector3 centroid = Centroid(points);
            float minZ = float.MaxValue, maxZ = float.MinValue, maxRsq = 0f;
            foreach (Vector3 p in points)
            {
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
                float dx = p.X - centroid.X, dy = p.Y - centroid.Y;
                float r = dx * dx + dy * dy;
                if (r > maxRsq) maxRsq = r;
            }

            center = new Vector3(centroid.X, centroid.Y, (minZ + maxZ) * 0.5f);
            radius = MathF.Max(MathF.Sqrt(maxRsq), 0.01f);
            halfHeight = MathF.Max((maxZ - minZ) * 0.5f, 0.01f);
        }

        private static Vector3 Centroid(IReadOnlyList<Vector3> points)
        {
            Vector3 sum = Vector3.Zero;
            foreach (Vector3 p in points) sum += p;
            return points.Count > 0 ? sum / points.Count : Vector3.Zero;
        }

        // Yaw of the dominant XY direction via the 2×2 covariance eigenvector.
        private static float PrincipalYaw(IReadOnlyList<Vector3> points)
        {
            if (points.Count < 3) return 0f;

            Vector3 c = Centroid(points);
            double cxx = 0, cxy = 0, cyy = 0;
            foreach (Vector3 p in points)
            {
                double dx = p.X - c.X, dy = p.Y - c.Y;
                cxx += dx * dx; cxy += dx * dy; cyy += dy * dy;
            }

            if (Math.Abs(cxy) < 1e-9 && Math.Abs(cxx - cyy) < 1e-9)
                return 0f;

            return 0.5f * MathF.Atan2(2f * (float)cxy, (float)(cxx - cyy));
        }
    }
}
