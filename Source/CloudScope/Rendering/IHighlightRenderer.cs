using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using CloudScope.Labeling;
using CloudScope.Sections;

namespace CloudScope.Rendering
{
    public interface IHighlightRenderer : IDisposable
    {
        void MarkDirty();
        void UpdatePreview(PointData[]? points, IReadOnlyList<int>? indices);
        void RenderPreview(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj, float pointSize, SectionClip section = default);
        void Render(IRenderFrameData frameData, PointData[] points, LabelManager labels, Func<PointAnnotation, Vector3> annotationColor, ref Matrix4 view, ref Matrix4 proj, float pointSize, SectionClip section = default);
    }
}
