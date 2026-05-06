#if MACOS
using System;
using CloudScope.Rendering;
using CloudScope.Selection;
using Metal;
using OpenTK.Mathematics;

namespace CloudScope.Platform.Metal
{
    internal abstract class MetalGizmoRendererBase : ISelectionGizmoRenderer
    {
        protected readonly MetalPrimitiveRenderer Renderer = new();
        private IMTLBuffer? axisBuffer;

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
            axisBuffer ??= Renderer.CreateStaticBuffer(new[]
            {
                -1f, 0f, 0f, 1f, 0f, 0f,
                0f, -1f, 0f, 0f, 1f, 0f,
                0f, 0f, -1f, 0f, 0f, 1f,
            });
        }

        protected void RenderAxis(Matrix4 mvp)
        {
            EnsureBase();
            for (int axis = 0; axis < 3; axis++)
                Renderer.Draw(axisBuffer, 2, MTLPrimitiveType.Line, mvp, AxisColor[axis], depthTest: false);
        }

        protected static (float nx, float ny) ScreenToNdc(float sx, float sy, float vpW, float vpH)
            => (sx / vpW * 2f - 1f, 1f - sy / vpH * 2f);

        public virtual void Dispose()
        {
            axisBuffer?.Dispose();
            Renderer.Dispose();
        }
    }

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

        private IMTLBuffer? edgeBuffer;
        private IMTLBuffer? faceBuffer;
        private IMTLBuffer? placementBuffer;

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            var box = (BoxSelectionTool)tool;
            EnsureResources();

            Matrix4 mvp = box.GetModelMatrix() * view * proj;
            Renderer.Draw(faceBuffer, 36, MTLPrimitiveType.Triangle, mvp, new Vector4(0.2f, 0.75f, 1f, 0.07f), depthTest: true);
            RenderAxis(mvp);
            Renderer.Draw(edgeBuffer, 24, MTLPrimitiveType.Line, mvp, new Vector4(0f, 0.8f, 1f, 0.8f), depthTest: true);
            Renderer.Draw(edgeBuffer, 24, MTLPrimitiveType.Line, mvp, new Vector4(0f, 0.8f, 1f, 0.18f), depthTest: false);
        }

        public void RenderPlacementRect(int x0, int y0, int x1, int y1, int viewportWidth, int viewportHeight)
        {
            EnsureResources();
            float nx0 = Math.Min(x0, x1) / (float)viewportWidth * 2f - 1f;
            float nx1 = Math.Max(x0, x1) / (float)viewportWidth * 2f - 1f;
            float ny0 = 1f - Math.Max(y0, y1) / (float)viewportHeight * 2f;
            float ny1 = 1f - Math.Min(y0, y1) / (float)viewportHeight * 2f;
            placementBuffer?.Dispose();
            placementBuffer = Renderer.CreateStaticBuffer(new[]
            {
                nx0,ny0,0f, nx1,ny0,0f, nx1,ny0,0f, nx1,ny1,0f,
                nx1,ny1,0f, nx0,ny1,0f, nx0,ny1,0f, nx0,ny0,0f,
            });
            Renderer.Draw(placementBuffer, 8, MTLPrimitiveType.Line, Matrix4.Identity, new Vector4(0f, 0.82f, 1f, 0.9f), depthTest: false);
        }

        public override void Dispose()
        {
            edgeBuffer?.Dispose();
            faceBuffer?.Dispose();
            placementBuffer?.Dispose();
            base.Dispose();
        }

        private void EnsureResources()
        {
            EnsureBase();
            edgeBuffer ??= Renderer.CreateStaticBuffer(Edges);
            faceBuffer ??= Renderer.CreateStaticBuffer(Faces);
        }
    }

    internal sealed class MetalSphereGizmoRenderer : MetalGizmoRendererBase
    {
        private IMTLBuffer? circleBuffer;
        private int circleVertexCount;

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            var sphere = (SphereSelectionTool)tool;
            if (sphere.Radius < 1e-5f)
                return;

            EnsureResources();
            Matrix4 mvp = Matrix4.CreateScale(sphere.Radius) * Matrix4.CreateTranslation(sphere.Center) * view * proj;
            RenderAxis(mvp);
            Renderer.Draw(circleBuffer, circleVertexCount, MTLPrimitiveType.Line, mvp, new Vector4(0.25f, 0.85f, 0.95f, 0.85f), depthTest: true);
            Renderer.Draw(circleBuffer, circleVertexCount, MTLPrimitiveType.Line, mvp, new Vector4(0.25f, 0.85f, 0.95f, 0.18f), depthTest: false);
        }

        public override void Dispose()
        {
            circleBuffer?.Dispose();
            base.Dispose();
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (circleBuffer != null)
                return;

            const int seg = 64;
            float[] data = new float[3 * seg * 2 * 3];
            int i = 0;
            AddCircle(data, ref i, seg, 0);
            AddCircle(data, ref i, seg, 1);
            AddCircle(data, ref i, seg, 2);
            circleVertexCount = i / 3;
            circleBuffer = Renderer.CreateStaticBuffer(data);
        }

        private static void AddCircle(float[] data, ref int i, int seg, int axis)
        {
            for (int s = 0; s < seg; s++)
            {
                float a0 = s * MathF.Tau / seg;
                float a1 = (s + 1) * MathF.Tau / seg;
                Add(data, ref i, axis, MathF.Cos(a0), MathF.Sin(a0));
                Add(data, ref i, axis, MathF.Cos(a1), MathF.Sin(a1));
            }
        }

        private static void Add(float[] data, ref int i, int axis, float c, float s)
        {
            if (axis == 0) { data[i++] = 0f; data[i++] = c; data[i++] = s; }
            else if (axis == 1) { data[i++] = c; data[i++] = 0f; data[i++] = s; }
            else { data[i++] = c; data[i++] = s; data[i++] = 0f; }
        }
    }

    internal sealed class MetalCylinderGizmoRenderer : MetalGizmoRendererBase
    {
        private IMTLBuffer? wireBuffer;
        private int wireVertexCount;

        public override void Render(ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            var cylinder = (CylinderSelectionTool)tool;
            if (cylinder.Radius < 1e-5f || cylinder.HalfHeight < 1e-5f)
                return;

            EnsureResources();
            Matrix4 model = Matrix4.CreateScale(cylinder.Radius, cylinder.Radius, cylinder.HalfHeight)
                * Matrix4.CreateFromQuaternion(cylinder.Rotation)
                * Matrix4.CreateTranslation(cylinder.Center);
            Matrix4 mvp = model * view * proj;
            RenderAxis(mvp);
            Renderer.Draw(wireBuffer, wireVertexCount, MTLPrimitiveType.Line, mvp, new Vector4(0.25f, 0.85f, 0.95f, 0.85f), depthTest: true);
            Renderer.Draw(wireBuffer, wireVertexCount, MTLPrimitiveType.Line, mvp, new Vector4(0.25f, 0.85f, 0.95f, 0.18f), depthTest: false);
        }

        public override void Dispose()
        {
            wireBuffer?.Dispose();
            base.Dispose();
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (wireBuffer != null)
                return;

            const int seg = 64;
            float[] data = new float[(seg * 4 + 8) * 3];
            int i = 0;
            for (int s = 0; s < seg; s++)
            {
                float a0 = s * MathF.Tau / seg;
                float a1 = (s + 1) * MathF.Tau / seg;
                Add(data, ref i, MathF.Cos(a0), MathF.Sin(a0), 1f);
                Add(data, ref i, MathF.Cos(a1), MathF.Sin(a1), 1f);
                Add(data, ref i, MathF.Cos(a0), MathF.Sin(a0), -1f);
                Add(data, ref i, MathF.Cos(a1), MathF.Sin(a1), -1f);
            }
            foreach (float a in new[] { 0f, MathF.PI * 0.5f, MathF.PI, MathF.PI * 1.5f })
            {
                Add(data, ref i, MathF.Cos(a), MathF.Sin(a), 1f);
                Add(data, ref i, MathF.Cos(a), MathF.Sin(a), -1f);
            }
            wireVertexCount = i / 3;
            wireBuffer = Renderer.CreateStaticBuffer(data);
        }

        private static void Add(float[] data, ref int i, float x, float y, float z)
        {
            data[i++] = x;
            data[i++] = y;
            data[i++] = z;
        }
    }
}
#endif
