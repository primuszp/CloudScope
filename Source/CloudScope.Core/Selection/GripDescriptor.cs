using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public enum GripKind
    {
        Center,
        AxisResize,
        CornerResize,
        RadiusResize,
        HeightResize,
        RotationRing
    }

    public enum GripConstraint
    {
        ViewPlane,
        Axis,
        Plane,
        Uniform,
        RotationAxis
    }

    /// <summary>
    /// Complete world-space description of a 3D grip. Renderers, hit testing,
    /// manipulators, and future object types consume the same contract.
    /// </summary>
    public readonly record struct GripDescriptor(
        int Index,
        GripKind Kind,
        Vector3 Position,
        Vector3 Direction,
        GripConstraint Constraint,
        int Axis = -1,
        int Sign = 0,
        bool IsPrimary = false)
    {
        public static GripDescriptor Center(int index, Vector3 position) =>
            new(index, GripKind.Center, position, Vector3.Zero, GripConstraint.ViewPlane);

        public static GripDescriptor AlongAxis(
            int index,
            GripKind kind,
            Vector3 position,
            Vector3 direction,
            int axis,
            int sign,
            bool primary = false) =>
            new(index, kind, position, direction.Normalized(), GripConstraint.Axis, axis, sign, primary);

        public static GripDescriptor Uniform(
            int index,
            GripKind kind,
            Vector3 position,
            Vector3 direction,
            int axis = -1,
            int sign = 0) =>
            new(index, kind, position, direction.Normalized(), GripConstraint.Uniform, axis, sign);

        public static GripDescriptor Ring(int index, Vector3 center, Vector3 axis, int localAxis) =>
            new(index, GripKind.RotationRing, center, axis.Normalized(), GripConstraint.RotationAxis, localAxis);
    }
}
