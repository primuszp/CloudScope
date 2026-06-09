using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public readonly record struct GripDragContext(
        GripDescriptor Grip,
        Vector3 StartCenter,
        int StartX,
        int StartY,
        float ViewZ);

    /// <summary>Shared, shape-independent 3D grip drag calculations.</summary>
    public static class GripManipulator3D
    {
        public static Vector3 Translation(
            GripDragContext context,
            OrbitCamera camera,
            int mouseX,
            int mouseY)
        {
            Vector3 delta = GripInteractionMath.ComputeWorldDragDelta(
                camera, context.StartX, context.StartY, mouseX, mouseY, context.ViewZ);

            return context.Grip.Constraint == GripConstraint.Axis
                ? ProjectToAxis(delta, context.Grip.Direction)
                : delta;
        }

        public static float AxisDistance(
            GripDragContext context,
            OrbitCamera camera,
            int mouseX,
            int mouseY)
            => Vector3.Dot(Translation(context, camera, mouseX, mouseY), context.Grip.Direction);

        public static float ScreenScale(
            GripDragContext context,
            OrbitCamera camera,
            int mouseX,
            int mouseY,
            float sensitivity)
        {
            Vector2 direction = GripInteractionMath.ComputeScreenDirection(
                camera, context.StartCenter, context.Grip.Position);
            float pixels = GripInteractionMath.ProjectMouseDelta(
                context.StartX, context.StartY, mouseX, mouseY, direction);
            return MathF.Max(1f + pixels * sensitivity, 0.01f);
        }

        public static float PointHitDistance(
            GripDescriptor grip,
            OrbitCamera camera,
            int mouseX,
            int mouseY) =>
            GripHitTestSupport.PointDistance(camera, grip.Position, mouseX, mouseY);

        private static Vector3 ProjectToAxis(Vector3 value, Vector3 axis) =>
            axis.LengthSquared < 1e-8f ? Vector3.Zero : axis * Vector3.Dot(value, axis);
    }
}
