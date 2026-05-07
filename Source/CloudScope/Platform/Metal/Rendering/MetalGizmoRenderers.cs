using System;
using System.Runtime.Versioning;
using CloudScope.Rendering;
using CloudScope.Selection;
using OpenTK.Mathematics;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal.Rendering
{
    [SupportedOSPlatform("macos")]
    internal abstract class MetalGizmoRendererBase : ISelectionGizmoRenderer
    {
        protected readonly MetalPrimitiveRenderer Renderer = new();
        private MTLBuffer _axisBuffer;

        protected static readonly Vector4[] AxisColor =
        {
            new(0.95f, 0.20f, 0.20f, 0.75f),
            new(0.20f, 0.95f, 0.30f, 0.75f),
            new(0.25f, 0.55f, 1.00f, 0.75f),
        };

        public abstract void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera);

        protected void EnsureBase()
        {
            Renderer.EnsureResources();
            if (_axisBuffer.NativePtr == IntPtr.Zero)
            {
                _axisBuffer = Renderer.CreateStaticBuffer(new[]
                {
                    -1f, 0f, 0f,  1f, 0f, 0f,
                     0f,-1f, 0f,  0f, 1f, 0f,
                     0f, 0f,-1f,  0f, 0f, 1f,
                });
            }
        }

        protected void RenderAxis(Matrix4 mvp)
        {
            EnsureBase();
            for (int axis = 0; axis < 3; axis++)
                Renderer.Draw(_axisBuffer, 2, MTLPrimitiveType.Line, mvp, AxisColor[axis], depthTest: false);
        }

        public virtual void Dispose() => Renderer.Dispose();
    }

    [SupportedOSPlatform("macos")]
    internal sealed class MetalBoxGizmoRenderer : MetalGizmoRendererBase, IBoxSelectionGizmoRenderer
    {
        private static readonly float[] Edges =
        {
            -1,-1,-1,  1,-1,-1,   1,-1,-1,  1, 1,-1,
             1, 1,-1, -1, 1,-1,  -1, 1,-1, -1,-1,-1,
            -1,-1, 1,  1,-1, 1,   1,-1, 1,  1, 1, 1,
             1, 1, 1, -1, 1, 1,  -1, 1, 1, -1,-1, 1,
            -1,-1,-1, -1,-1, 1,   1,-1,-1,  1,-1, 1,
             1, 1,-1,  1, 1, 1,  -1, 1,-1, -1, 1, 1,
        };

        private static readonly float[] Faces =
        {
            -1,-1,-1, -1, 1,-1, -1, 1, 1,   -1,-1,-1, -1, 1, 1, -1,-1, 1,
             1,-1,-1,  1, 1,-1,  1, 1, 1,    1,-1,-1,  1, 1, 1,  1,-1, 1,
            -1,-1,-1,  1,-1,-1,  1,-1, 1,   -1,-1,-1,  1,-1, 1, -1,-1, 1,
            -1, 1,-1,  1, 1,-1,  1, 1, 1,   -1, 1,-1,  1, 1, 1, -1, 1, 1,
            -1,-1,-1,  1,-1,-1,  1, 1,-1,   -1,-1,-1,  1, 1,-1, -1, 1,-1,
            -1,-1, 1,  1,-1, 1,  1, 1, 1,   -1,-1, 1,  1, 1, 1, -1, 1, 1,
        };

        private MTLBuffer _edgeBuffer;
        private MTLBuffer _faceBuffer;
        private MTLBuffer _placementBuffer;

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            var box = (BoxSelectionTool)tool;
            EnsureResources();
            Matrix4 mvp = box.GetModelMatrix() * view * proj;
            Renderer.Draw(_faceBuffer, 36, MTLPrimitiveType.Triangle, mvp, new Vector4(0.2f, 0.75f, 1f, 0.07f), depthTest: true);
            RenderAxis(mvp);
            Renderer.Draw(_edgeBuffer, 24, MTLPrimitiveType.Line, mvp, new Vector4(0f, 0.8f, 1f, 0.8f),  depthTest: true);
            Renderer.Draw(_edgeBuffer, 24, MTLPrimitiveType.Line, mvp, new Vector4(0f, 0.8f, 1f, 0.18f), depthTest: false);
        }

        public void RenderPlacementRect(int x0, int y0, int x1, int y1, int viewportWidth, int viewportHeight)
        {
            EnsureResources();
            float nx0 = Math.Min(x0, x1) / (float)viewportWidth  * 2f - 1f;
            float nx1 = Math.Max(x0, x1) / (float)viewportWidth  * 2f - 1f;
            float ny0 = 1f - Math.Max(y0, y1) / (float)viewportHeight * 2f;
            float ny1 = 1f - Math.Min(y0, y1) / (float)viewportHeight * 2f;
            _placementBuffer = Renderer.CreateStaticBuffer(new[]
            {
                nx0,ny0,0f, nx1,ny0,0f, nx1,ny0,0f, nx1,ny1,0f,
                nx1,ny1,0f, nx0,ny1,0f, nx0,ny1,0f, nx0,ny0,0f,
            });
            Renderer.Draw(_placementBuffer, 8, MTLPrimitiveType.Line,
                Matrix4.Identity, new Vector4(0f, 0.82f, 1f, 0.9f), depthTest: false);
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (_edgeBuffer.NativePtr == IntPtr.Zero) _edgeBuffer = Renderer.CreateStaticBuffer(Edges);
            if (_faceBuffer.NativePtr == IntPtr.Zero) _faceBuffer = Renderer.CreateStaticBuffer(Faces);
        }
    }

    [SupportedOSPlatform("macos")]
    internal sealed class MetalSphereGizmoRenderer : MetalGizmoRendererBase
    {
        private MTLBuffer _circleBuffer;
        private int _circleVertexCount;

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            var sphere = (SphereSelectionTool)tool;
            if (sphere.Radius < 1e-5f) return;

            EnsureResources();
            Matrix4 mvp = Matrix4.CreateScale(sphere.Radius)
                        * Matrix4.CreateTranslation(sphere.Center)
                        * view * proj;
            RenderAxis(mvp);
            Renderer.Draw(_circleBuffer, _circleVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.85f), depthTest: true);
            Renderer.Draw(_circleBuffer, _circleVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.18f), depthTest: false);
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (_circleBuffer.NativePtr != IntPtr.Zero) return;

            const int seg = 64;
            float[] data = new float[3 * seg * 2 * 3];
            int i = 0;
            AddCircle(data, ref i, seg, 0);
            AddCircle(data, ref i, seg, 1);
            AddCircle(data, ref i, seg, 2);
            _circleVertexCount = i / 3;
            _circleBuffer      = Renderer.CreateStaticBuffer(data);
        }

        private static void AddCircle(float[] data, ref int i, int seg, int axis)
        {
            for (int s = 0; s < seg; s++)
            {
                float a0 = s       * MathF.Tau / seg;
                float a1 = (s + 1) * MathF.Tau / seg;
                AddCirclePt(data, ref i, axis, MathF.Cos(a0), MathF.Sin(a0));
                AddCirclePt(data, ref i, axis, MathF.Cos(a1), MathF.Sin(a1));
            }
        }

        private static void AddCirclePt(float[] data, ref int i, int axis, float c, float s)
        {
            if (axis == 0) { data[i++] = 0f; data[i++] = c; data[i++] = s; }
            else if (axis == 1) { data[i++] = c; data[i++] = 0f; data[i++] = s; }
            else { data[i++] = c; data[i++] = s; data[i++] = 0f; }
        }
    }

    [SupportedOSPlatform("macos")]
    internal sealed class MetalCylinderGizmoRenderer : MetalGizmoRendererBase
    {
        private MTLBuffer _wireBuffer;
        private int _wireVertexCount;

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            var cyl = (CylinderSelectionTool)tool;
            if (cyl.Radius < 1e-5f || cyl.HalfHeight < 1e-5f) return;

            EnsureResources();
            Matrix4 model = Matrix4.CreateScale(cyl.Radius, cyl.Radius, cyl.HalfHeight)
                          * Matrix4.CreateFromQuaternion(cyl.Rotation)
                          * Matrix4.CreateTranslation(cyl.Center);
            Matrix4 mvp = model * view * proj;
            RenderAxis(mvp);
            Renderer.Draw(_wireBuffer, _wireVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.85f), depthTest: true);
            Renderer.Draw(_wireBuffer, _wireVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.18f), depthTest: false);
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (_wireBuffer.NativePtr != IntPtr.Zero) return;

            const int seg = 64;
            float[] data = new float[(seg * 4 + 8) * 3];
            int i = 0;
            for (int s = 0; s < seg; s++)
            {
                float a0 = s       * MathF.Tau / seg;
                float a1 = (s + 1) * MathF.Tau / seg;
                AddPt(data, ref i, MathF.Cos(a0), MathF.Sin(a0),  1f);
                AddPt(data, ref i, MathF.Cos(a1), MathF.Sin(a1),  1f);
                AddPt(data, ref i, MathF.Cos(a0), MathF.Sin(a0), -1f);
                AddPt(data, ref i, MathF.Cos(a1), MathF.Sin(a1), -1f);
            }
            foreach (float a in new[] { 0f, MathF.PI * 0.5f, MathF.PI, MathF.PI * 1.5f })
            {
                AddPt(data, ref i, MathF.Cos(a), MathF.Sin(a),  1f);
                AddPt(data, ref i, MathF.Cos(a), MathF.Sin(a), -1f);
            }
            _wireVertexCount = i / 3;
            _wireBuffer      = Renderer.CreateStaticBuffer(data);
        }

        private static void AddPt(float[] data, ref int i, float x, float y, float z)
        { data[i++] = x; data[i++] = y; data[i++] = z; }
    }
}
