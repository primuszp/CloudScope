using OpenTK.Mathematics;

namespace CloudScope.Selection;

/// <summary>A grip target whose complete geometry can be captured for Undo/Escape.</summary>
public interface ITransactionalGripTarget : IGripTarget
{
    string EditKey { get; }
    string EditDescription { get; }
    Vector3 DragAnchor { get; }
    void UpdateHandleDragTo(Vector3 worldPoint);
    object CaptureState();
    void RestoreState(object state);
    bool StatesEqual(object first, object second);
}
