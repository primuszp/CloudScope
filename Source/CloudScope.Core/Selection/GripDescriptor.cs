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

    public readonly record struct GripDescriptor(
        int Index,
        GripKind Kind,
        int Axis = -1,
        int Sign = 0,
        bool IsPrimary = false);
}
