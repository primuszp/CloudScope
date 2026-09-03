using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>
/// The one set of world-axis colours in the viewer: X red, Y green, Z blue, as AutoCAD paints
/// them. The pivot indicator, the selection gizmos, the 3D crosshair and axis snapping all read
/// the same axis off the screen, so they must not each carry their own shade of red.
/// </summary>
public static class AxisPalette
{
    public static readonly Vector3 X = new(0.95f, 0.20f, 0.20f);
    public static readonly Vector3 Y = new(0.20f, 0.95f, 0.30f);
    public static readonly Vector3 Z = new(0.25f, 0.55f, 1.00f);

    /// <summary>0 for X, 1 for Y, anything else for Z.</summary>
    public static Vector3 Of(int axis) => axis switch
    {
        0 => X,
        1 => Y,
        _ => Z
    };

    public static Vector4 Of(int axis, float alpha) => new(Of(axis), alpha);
}
