using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>
/// Everything a point cloud renderer needs to know about the frame it is drawing into.
/// </summary>
/// <param name="View">World to view transform.</param>
/// <param name="Projection">View to clip transform.</param>
/// <param name="ViewportWidth">Viewport width in pixels.</param>
/// <param name="ViewportHeight">Viewport height in pixels.</param>
/// <param name="PointSize">Point sprite size in pixels.</param>
public readonly record struct PointRenderView(
    Matrix4 View,
    Matrix4 Projection,
    int ViewportWidth,
    int ViewportHeight,
    float PointSize);
