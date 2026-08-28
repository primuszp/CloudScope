namespace CloudScope.Selection;

/// <summary>
/// Shared hover/hot/drag state machine for every grip-editable object.
/// Pointer capture and undo ownership stay with the caller, while geometry stays with
/// the target.
/// </summary>
public sealed class GripInteractionSession
{
    public IGripTarget? Target { get; private set; }
    public bool IsDragging => Target?.IsHandleDragging == true;

    public void SetTarget(IGripTarget? target)
    {
        if (ReferenceEquals(Target, target)) return;
        if (IsDragging) Target!.CancelHandleDrag();
        if (Target != null) Target.HoveredHandle = -1;
        Target = target;
    }

    public int UpdateHover(int mouseX, int mouseY, OrbitCamera camera, float threshold = 12f)
    {
        if (Target == null || IsDragging) return -1;
        return Target.HoveredHandle = Target.HitTestHandles(mouseX, mouseY, camera, threshold);
    }

    public bool TryBegin(int mouseX, int mouseY, OrbitCamera camera, bool allowBodyDrag = false)
    {
        if (Target == null) return false;
        int handle = Target.HitTestHandles(mouseX, mouseY, camera);
        if (handle < 0 && allowBodyDrag && Target.HitTestBody(mouseX, mouseY, camera))
            handle = Target.CenterGripIndex;
        if (handle < 0) return false;

        Target.BeginHandleDrag(handle, mouseX, mouseY, camera);
        return Target.IsHandleDragging;
    }

    public void Update(int mouseX, int mouseY, OrbitCamera camera)
    {
        if (IsDragging) Target!.UpdateHandleDrag(mouseX, mouseY, camera);
        else UpdateHover(mouseX, mouseY, camera);
    }

    public bool Commit()
    {
        if (!IsDragging) return false;
        Target!.EndHandleDrag();
        return true;
    }

    public bool Cancel()
    {
        if (!IsDragging) return false;
        Target!.CancelHandleDrag();
        return true;
    }
}
