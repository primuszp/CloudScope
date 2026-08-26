using System;
using CloudScope.Store;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>
/// One cloud among several on screen: a store, whether it is drawn, and how it is tinted.
/// </summary>
/// <remarks>
/// A layer owns no GPU memory of its own. The whole point of layering the streamed path is
/// that the budget is one pool: a second cloud does not halve the first one's detail, it
/// competes for the same pages by how close it is to the camera, and a layer nobody is looking
/// at loses its pages to one that is being looked at.
/// </remarks>
public sealed class PointTileLayer
{
    internal PointTileLayer(string name, PointTileStore store)
    {
        Name = name;
        Store = store;
        Roots = PointTileTraversal.FindRoots(store.Nodes);
    }

    public string Name { get; }

    public PointTileStore Store { get; }

    /// <summary>Where a traversal of this layer starts.</summary>
    internal int[] Roots { get; }

    public bool Visible { get; set; } = true;

    /// <summary>
    /// Multiplied into the cloud's own color, so white leaves it as it was stored.
    /// </summary>
    /// <remarks>
    /// A tint rather than a replacement: telling two overlapping surveys apart wants the
    /// colors pushed apart, not the intensity or classification thrown away.
    /// </remarks>
    public Vector3 Tint { get; set; } = Vector3.One;

    public long PointCount => Store.Header.PointCount;
}
