using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

internal static class OverlayLayout
{
    public static Vector2 CrosshairExtent(int width, int height) =>
        new(15f / width, 15f / height);

    public static (Vector2 Center, Vector2 Extent) ModeIndicator(int width, int height) =>
        (new Vector2(-1f + 30f / width, 1f - 30f / height),
         new Vector2(8f / width, 8f / height));

    public static Vector3 ModeColor(SelectionToolType toolType) => toolType switch
    {
        SelectionToolType.Sphere => new Vector3(1f, 0.6f, 0.15f),
        SelectionToolType.Cylinder => new Vector3(0.60f, 0.25f, 1f),
        _ => new Vector3(0f, 0.8f, 1f)
    };
}
