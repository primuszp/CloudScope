using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using CloudScope.Labeling;

namespace CloudScope.Rendering
{
    public interface IHighlightRenderer : IDisposable
    {
        void MarkDirty();
        void UpdatePreview(PointData[]? points, IReadOnlyList<int>? indices);
        void RenderPreview(ref Matrix4 view, ref Matrix4 proj, float pointSize);
        void Render(PointData[] points, LabelManager labels, ref Matrix4 view, ref Matrix4 proj, float pointSize);
    }
}
