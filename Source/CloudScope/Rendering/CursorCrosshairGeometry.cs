using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>
/// The AutoCAD-style 3D crosshair that follows the point cursor: one arm along each world
/// axis plus a pick box at the intersection. The arms are the axes the point input can lock
/// onto, so the crosshair doubles as a legend for axis tracking - an arm lights up in the
/// same colour <see cref="GripOverlayGeometry.SnapColor"/> gives that axis snap.
/// </summary>
public static class CursorCrosshairGeometry
{
    /// <summary>Half-length of one crosshair arm, in logical pixels.</summary>
    public const float ArmPixels = 46f;

    /// <summary>Half-size of the pick box at the crosshair centre, in logical pixels.</summary>
    public const float PickBoxPixels = 5f;

    public static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

    public static ObjectSnapKind KindOf(int axis) => axis switch
    {
        0 => ObjectSnapKind.AxisX,
        1 => ObjectSnapKind.AxisY,
        _ => ObjectSnapKind.AxisZ
    };

    /// <summary>The two endpoints of one arm, sized so it keeps its pixel length at any depth.</summary>
    public static Vector3[] Arm(Vector3 position, int axis, OrbitCamera camera, float displayScale = 1f)
    {
        Vector3 half = Axes[axis] * ArmLength(position, camera, displayScale);
        return [position - half, position + half];
    }

    /// <summary>
    /// The pick box as a closed loop. Unlike a grip it is not part of the drawing but the
    /// aperture the cursor picks through, so it is built on the camera axes: it faces the
    /// screen and stays an axis-aligned square of the same pixel size in every view, however
    /// the model is rotated underneath it.
    /// </summary>
    public static Vector3[] PickBox(Vector3 position, OrbitCamera camera, float displayScale = 1f)
    {
        float radius = MathF.Max(
            camera.WorldUnitsPerPixel(position) * PickBoxPixels * Scale(displayScale), 0.00001f);
        Vector3 right = camera.CameraRight * radius;
        Vector3 up = camera.CameraUp * radius;
        return
        [
            position - right - up,
            position + right - up,
            position + right + up,
            position - right + up
        ];
    }

    /// <summary>
    /// Dimmed while the cursor floats free, full strength on the axis the input is locked to,
    /// which is how a CAD crosshair reports the lock without a separate readout.
    /// </summary>
    public static Vector4 ArmColor(int axis, ObjectSnapKind snapped)
    {
        bool active = snapped == KindOf(axis);
        return GripOverlayGeometry.SnapColor(KindOf(axis), active ? 0.95f : 0.38f);
    }

    private static float ArmLength(Vector3 position, OrbitCamera camera, float displayScale)
        => MathF.Max(camera.WorldUnitsPerPixel(position) * ArmPixels * Scale(displayScale), 0.00001f);

    private static float Scale(float displayScale)
        => float.IsFinite(displayScale) && displayScale > 0f ? displayScale : 1f;
}
