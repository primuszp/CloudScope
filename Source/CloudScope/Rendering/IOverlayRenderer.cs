using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using CloudScope.Selection;
using CloudScope.Sections;

namespace CloudScope.Rendering
{
    public interface IOverlayRenderer : IDisposable
    {
        void Initialize();
        void RenderPivotIndicator(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj, OrbitCamera camera, Vector3 pivot, float fade, float flash);
        void RenderCenterCrosshair(IRenderFrameData frameData, int width, int height, float alpha);
        void RenderModeIndicator(IRenderFrameData frameData, int width, int height, SelectionToolType toolType);
        void RenderViewportBorder(IRenderFrameData frameData, int width, int height, bool active);
        void RenderSectionGuide(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj, SectionDefinition section);
        void RenderGrips(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj,
            OrbitCamera camera, IReadOnlyList<GripDescriptor> grips, int hovered, int active);
        void RenderPolyline(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj,
            IReadOnlyList<Vector3> points, bool closed, Vector4 color, float widthPixels, bool depthTest = true,
            float dashPixels = 0f);
        void RenderSnapIndicator(IRenderFrameData frameData, ref Matrix4 view, ref Matrix4 proj,
            OrbitCamera camera, ObjectSnapResult snap);
    }
}
