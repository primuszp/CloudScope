namespace CloudScope.Selection
{
    public enum InteractionMode
    {
        Navigate,
        Label
    }

    public enum SelectionToolType
    {
        Box,
        Sphere
    }

    /// <summary>
    /// Which edit gesture is currently in progress (Blender-style).
    /// Set by pressing G/S/R while a tool IsEditing, activated on mouse-down.
    /// </summary>
    public enum EditAction
    {
        None,
        Grab,
        Scale,
        Rotate
    }
}
