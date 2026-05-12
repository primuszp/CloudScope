using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public enum GripVisualRole
    {
        CenterHandle,
        StandardHandle,
        AxisHandle,
        PrimaryHandle,
        RotationRing
    }

    public readonly record struct GripVisualDescriptor(
        GripVisualRole Role,
        Vector4 Color,
        float LineWidth = 0f);
}
