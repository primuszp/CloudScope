using OpenTK.Mathematics;

namespace CloudScope.Selection
{
    /// <summary>
    /// Restricts which grips of a selection volume are shown and interactive when a
    /// viewport looks along a world axis (an orthographic Top/Bottom, Left/Right or
    /// Front/Back view). In such a view only the grips that are meaningful in the
    /// view plane are useful:
    ///   • in-plane resize grips     (their direction is perpendicular to the view axis),
    ///   • the single rotation ring   (its axis is parallel to the view axis),
    ///   • the center grip            (always — used for in-plane translation).
    /// Out-of-plane (depth) resize grips, the other rotation rings, and the ambiguous
    /// corner grips are hidden. The same predicate drives rendering and hit testing so
    /// what is drawn is exactly what can be grabbed.
    /// </summary>
    public readonly struct GripViewConstraint
    {
        private const float PerpendicularTolerance = 0.30f; // |dot| below ⇒ in the view plane
        private const float ParallelTolerance      = 0.85f; // |dot| above ⇒ aligned with view axis

        private GripViewConstraint(Vector3 viewAxis)
        {
            IsConstrained = true;
            ViewAxis = viewAxis;
        }

        public bool IsConstrained { get; }
        public Vector3 ViewAxis { get; }

        public static GripViewConstraint None => default;

        public static GripViewConstraint AlongWorldAxis(int axis) => new(axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            _ => Vector3.UnitZ
        });

        public bool IsVisible(GripDescriptor grip)
        {
            if (!IsConstrained)
                return true;

            switch (grip.Kind)
            {
                case GripKind.Center:
                    return true;
                case GripKind.RotationRing:
                    return MathF.Abs(Vector3.Dot(grip.Direction, ViewAxis)) > ParallelTolerance;
                case GripKind.CornerResize:
                    return false;
                default: // AxisResize, RadiusResize, HeightResize
                    return MathF.Abs(Vector3.Dot(grip.Direction, ViewAxis)) < PerpendicularTolerance;
            }
        }
    }
}
