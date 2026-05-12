using System;
using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    internal static class GripInteractionMath
    {
        public static Vector3 ComputeWorldDragDelta(
            OrbitCamera camera,
            int startX,
            int startY,
            int currentX,
            int currentY,
            float viewZ)
        {
            Vector3 startWorld = camera.ScreenToWorldAtDepth(startX, startY, viewZ);
            Vector3 currentWorld = camera.ScreenToWorldAtDepth(currentX, currentY, viewZ);
            return currentWorld - startWorld;
        }

        public static Vector2 ComputeScreenDirection(
            OrbitCamera camera,
            Vector3 origin,
            Vector3 target)
        {
            var (cx, cy, _) = camera.WorldToScreen(origin);
            var (px, py, _) = camera.WorldToScreen(target);
            float dx = px - cx;
            float dy = py - cy;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            return len > 0.5f ? new Vector2(dx / len, dy / len) : new Vector2(1f, 0f);
        }

        public static float ProjectMouseDelta(
            int startX,
            int startY,
            int currentX,
            int currentY,
            Vector2 screenDirection)
            => (currentX - startX) * screenDirection.X
             + (currentY - startY) * screenDirection.Y;

        public static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f)
                return MathF.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));

            float t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lenSq, 0f, 1f);
            float qx = ax + t * dx - px;
            float qy = ay + t * dy - py;
            return MathF.Sqrt(qx * qx + qy * qy);
        }

        public static float RingScreenDistance(
            OrbitCamera camera,
            Vector3 center,
            Quaternion rotation,
            int axis,
            float radius,
            int mouseX,
            int mouseY)
        {
            const int SegmentCount = 32;
            Matrix3 inverseRotation = Matrix3.Transpose(Matrix3.CreateFromQuaternion(rotation));

            float minDistance = float.MaxValue;
            float previousX = 0f;
            float previousY = 0f;
            bool previousVisible = false;

            for (int index = 0; index <= SegmentCount; index++)
            {
                float angle = index * MathF.Tau / SegmentCount;
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);
                Vector3 local = axis switch
                {
                    0 => new Vector3(0f, cos, sin),
                    1 => new Vector3(cos, 0f, sin),
                    _ => new Vector3(cos, sin, 0f),
                } * radius;

                var (screenX, screenY, behind) = camera.WorldToScreen(center + inverseRotation * local);
                if (!behind && previousVisible)
                {
                    float distance = SegmentDistance(mouseX, mouseY, previousX, previousY, screenX, screenY);
                    if (distance < minDistance)
                        minDistance = distance;
                }

                previousX = screenX;
                previousY = screenY;
                previousVisible = !behind;
            }

            return minDistance;
        }

        public static Quaternion RotateAroundRingDrag(
            OrbitCamera camera,
            Vector3 center,
            Quaternion startRotation,
            int axis,
            int startX,
            int startY,
            int currentX,
            int currentY)
        {
            Matrix3 inverseRotation = Matrix3.Transpose(Matrix3.CreateFromQuaternion(startRotation));
            Vector3 localAxis = axis switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                _ => Vector3.UnitZ
            };
            Vector3 worldAxis = (inverseRotation * localAxis).Normalized();

            float viewZ = camera.WorldToViewZ(center);
            Vector3 p0 = camera.ScreenToWorldAtDepth(startX, startY, viewZ);
            Vector3 p1 = camera.ScreenToWorldAtDepth(currentX, currentY, viewZ);

            Vector3 v0 = p0 - center;
            v0 -= Vector3.Dot(v0, worldAxis) * worldAxis;
            Vector3 v1 = p1 - center;
            v1 -= Vector3.Dot(v1, worldAxis) * worldAxis;
            if (v0.LengthSquared < 1e-8f || v1.LengthSquared < 1e-8f)
                return startRotation;

            v0 = v0.Normalized();
            v1 = v1.Normalized();
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(v0, v1), -1f, 1f));
            if (Vector3.Dot(Vector3.Cross(v0, v1), worldAxis) < 0f)
                angle = -angle;

            Quaternion rotation = Quaternion.FromAxisAngle(worldAxis, angle) * startRotation;
            rotation.Normalize();
            return rotation;
        }
    }
}
