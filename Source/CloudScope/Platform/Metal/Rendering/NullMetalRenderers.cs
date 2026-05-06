using System;
using System.Collections.Generic;
using CloudScope.Labeling;
using CloudScope.Rendering;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Platform.Metal
{
    internal sealed class NullPointCloudRenderer : IPointCloudRenderer
    {
        public int PointCount { get; private set; }
        public void Initialize() { }
        public void Upload(PointData[] points) => PointCount = points.Length;
        public int Render(ref Matrix4 view, ref Matrix4 projection, float pointSize, double halfViewSize, float cloudRadius) => 0;
        public void Dispose() { }
    }

    internal sealed class NullHighlightRenderer : IHighlightRenderer
    {
        public void MarkDirty() { }
        public void UpdatePreview(PointData[]? points, IReadOnlyList<int>? indices) { }
        public void RenderPreview(ref Matrix4 view, ref Matrix4 proj, float pointSize) { }
        public void Render(PointData[] points, LabelManager labels, ref Matrix4 view, ref Matrix4 proj, float pointSize) { }
        public void Dispose() { }
    }

    internal sealed class NullOverlayRenderer : IOverlayRenderer
    {
        public void Initialize() { }
        public void RenderPivotIndicator(ref Matrix4 view, ref Matrix4 proj, OrbitCamera camera, Vector3 pivot, float fade, float flash) { }
        public void RenderCenterCrosshair(int width, int height, float alpha) { }
        public void RenderModeIndicator(int width, int height, SelectionToolType toolType) { }
        public void Dispose() { }
    }

    internal sealed class NullSelectionGizmoRenderer : IBoxSelectionGizmoRenderer
    {
        public void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera) { }
        public void RenderPlacementRect(int x0, int y0, int x1, int y1, int viewportWidth, int viewportHeight) { }
        public void Dispose() { }
    }

    internal sealed class NullDepthPicker : IDepthPicker
    {
        public float ReadDepth(int x, int y) => 1f;

        public int ReadDepthWindow(int x, int y, int width, int height, float[] destination)
        {
            Array.Fill(destination, 1f);
            return Math.Min(destination.Length, width * height);
        }
    }
}
