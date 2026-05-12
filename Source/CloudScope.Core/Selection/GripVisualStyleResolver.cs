using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    public static class GripVisualStyleResolver
    {
        private static readonly Vector4 HoverColor = new(1f, 1f, 0.15f, 1f);
        private static readonly Vector4 ActiveColor = new(1f, 0.78f, 0.20f, 1f);
        private static readonly Vector4 CenterColor = new(0.3f, 1f, 0.45f, 0.85f);
        private static readonly Vector4 HandleColor = new(0.9f, 0.9f, 0.9f, 0.80f);
        private static readonly Vector4 PrimaryColor = new(1f, 0.6f, 0f, 0.95f);

        public static GripVisualDescriptor ResolvePointGrip(
            GripDescriptor grip,
            bool hovered,
            bool active = false)
        {
            if (active)
                return new GripVisualDescriptor(
                    grip.IsPrimary ? GripVisualRole.PrimaryHandle : ResolvePointRole(grip),
                    ActiveColor,
                    grip.IsPrimary ? 4f : 2f);

            if (hovered)
                return new GripVisualDescriptor(
                    grip.IsPrimary ? GripVisualRole.PrimaryHandle : ResolvePointRole(grip),
                    HoverColor,
                    grip.IsPrimary ? 4f : 0f);

            if (grip.IsPrimary)
                return new GripVisualDescriptor(GripVisualRole.PrimaryHandle, PrimaryColor, 3f);

            return grip.Kind == GripKind.Center
                ? new GripVisualDescriptor(GripVisualRole.CenterHandle, CenterColor)
                : new GripVisualDescriptor(GripVisualRole.StandardHandle, HandleColor);
        }

        public static GripVisualDescriptor ResolveAxisGrip(
            GripDescriptor grip,
            bool hovered,
            bool emphasizePrimary,
            Vector4 axisColor,
            bool active = false)
        {
            if (active)
                return new GripVisualDescriptor(GripVisualRole.AxisHandle, ActiveColor, 4f);

            if (hovered)
                return new GripVisualDescriptor(GripVisualRole.AxisHandle, HoverColor, 3f);

            if (grip.IsPrimary && emphasizePrimary)
                return new GripVisualDescriptor(GripVisualRole.PrimaryHandle, PrimaryColor, 2f);

            return new GripVisualDescriptor(GripVisualRole.AxisHandle, axisColor with { W = 0.90f }, 2f);
        }

        public static GripVisualDescriptor ResolveRing(
            bool hovered,
            Vector4 axisColor,
            bool active = false)
            => active
                ? new GripVisualDescriptor(GripVisualRole.RotationRing, ActiveColor, 4f)
                : hovered
                ? new GripVisualDescriptor(GripVisualRole.RotationRing, HoverColor, 3f)
                : new GripVisualDescriptor(GripVisualRole.RotationRing, axisColor with { W = 0.80f }, 2f);

        private static GripVisualRole ResolvePointRole(GripDescriptor grip)
            => grip.Kind == GripKind.Center
                ? GripVisualRole.CenterHandle
                : GripVisualRole.StandardHandle;
    }
}
