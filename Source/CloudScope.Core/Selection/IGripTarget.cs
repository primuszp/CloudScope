using System.Collections.Generic;
using OpenTK.Mathematics;

namespace CloudScope.Selection;

/// <summary>
/// Object-independent contract for AutoCAD-style grip editing. A selection volume,
/// section plane, measurement or future drawing entity can all be driven by the same
/// pointer state machine without teaching the viewport about that object's geometry.
/// </summary>
public interface IGripTarget
{
    IReadOnlyList<GripDescriptor> Grips { get; }
    int CenterGripIndex { get; }
    int HoveredHandle { get; set; }
    int ActiveHandle { get; }
    bool IsHandleDragging { get; }

    bool IsGripVisible(int index);
    bool HitTestBody(int mouseX, int mouseY, OrbitCamera camera);
    int HitTestHandles(int mouseX, int mouseY, OrbitCamera camera, float threshold = 12f);
    GripDescriptor GetGrip(int handle);
    bool TryGetGrip(int handle, out GripDescriptor grip);

    void BeginHandleDrag(int handle, int mouseX, int mouseY, OrbitCamera camera);
    void UpdateHandleDrag(int mouseX, int mouseY, OrbitCamera camera);
    void EndHandleDrag();
    void CancelHandleDrag();
}
