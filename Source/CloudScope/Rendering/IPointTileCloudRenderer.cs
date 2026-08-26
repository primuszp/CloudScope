using System;
using System.Collections.Generic;
using CloudScope.Loading;
using CloudScope.Store;

namespace CloudScope.Rendering;

/// <summary>
/// Draws a cloud straight off an on-disk <see cref="PointTileStore"/>, streaming the cells the
/// camera is looking at.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="IPointCloudRenderer"/>: that one is handed the whole cloud as a
/// <see cref="PointCloudRenderData"/>, which is exactly the thing a two-billion-point file
/// cannot produce. The two paths live side by side because selection and labelling still work
/// on an in-memory <c>PointData[]</c>, so a cloud small enough to hold in memory keeps every
/// feature while a large one is at least viewable.
/// </remarks>
public interface IPointTileCloudRenderer : IDisposable
{
    /// <summary>Points drawn in the most recent frame.</summary>
    long DrawnPointCount { get; }

    /// <summary>Cells currently on the GPU.</summary>
    int ResidentCellCount { get; }

    /// <summary>Cells the last frame wanted but that have not been read off disk yet.</summary>
    int PendingCellCount { get; }

    void Initialize();

    /// <summary>
    /// Attaches the layers to draw. The renderer does not take ownership of their stores.
    /// </summary>
    /// <remarks>
    /// The list is held live, not copied: toggling a layer's visibility or changing its tint
    /// takes effect on the next frame without reopening anything. Adding or removing a layer
    /// does need a reopen, since the working set is sized from what is on screen.
    /// </remarks>
    void Open(IReadOnlyList<PointTileLayer> layers);

    /// <summary>Detaches the store and releases the working set, drawing nothing until reopened.</summary>
    void Close();

    void UpdateColorSource(ColorSource source);

    /// <summary>Draws the store and returns how many points went to the GPU.</summary>
    int Render(IRenderFrameData frameData, in PointRenderView view);
}
