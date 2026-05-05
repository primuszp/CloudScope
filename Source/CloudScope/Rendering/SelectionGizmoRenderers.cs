namespace CloudScope.Rendering
{
    public sealed class SelectionGizmoRenderers
    {
        public SelectionGizmoRenderers(
            IBoxSelectionGizmoRenderer box,
            ISelectionGizmoRenderer sphere,
            ISelectionGizmoRenderer cylinder)
        {
            Box = box;
            Sphere = sphere;
            Cylinder = cylinder;
            All = new ISelectionGizmoRenderer[] { box, sphere, cylinder };
        }

        public IBoxSelectionGizmoRenderer Box { get; }
        public ISelectionGizmoRenderer Sphere { get; }
        public ISelectionGizmoRenderer Cylinder { get; }
        public ISelectionGizmoRenderer[] All { get; }
    }
}
