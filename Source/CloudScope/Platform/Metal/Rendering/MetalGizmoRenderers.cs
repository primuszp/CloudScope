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

        public abstract void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera);

        protected bool TrySetFrame(IRenderFrameData frameData)
        {
            if (frameData is not MetalFrameState frame)
                return false;

            Renderer.SetFrame(frame);
            return true;
        }

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

        public virtual void Dispose()
        {
            MetalPrimitiveRenderer.Release(ref _axisBuffer);
            Renderer.Dispose();
        }
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
        private MTLBuffer _placementFillBuffer;
        private MTLBuffer _placementLineBuffer;
        private MTLBuffer _dynamicBuffer;
        private readonly float[] _lineBuf = new float[6];
        private readonly float[] _arrowBuf = new float[9];
        private readonly float[] _diamondBuf = new float[18];
        private readonly float[] _ringBuf = new float[64 * 6];

        public override void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            if (!TrySetFrame(frameData)) return;
            var box = (BoxSelectionTool)tool;
            EnsureResources();
            Matrix4 mvp = box.GetModelMatrix() * view * proj;
            Renderer.Draw(_faceBuffer, 36, MTLPrimitiveType.Triangle, mvp, new Vector4(0.2f, 0.75f, 1f, 0.07f), depthTest: true);
            RenderAxis(mvp);
            Renderer.Draw(_edgeBuffer, 24, MTLPrimitiveType.Line, mvp, new Vector4(0f, 0.8f, 1f, 0.8f),  depthTest: true);
            Renderer.Draw(_edgeBuffer, 24, MTLPrimitiveType.Line, mvp, new Vector4(0f, 0.8f, 1f, 0.18f), depthTest: false);
            RenderFaceArrows(box, camera);
            RenderCornerHandles(box, camera);
            RenderRings(box, camera);
            if (box.IsFlat) RenderExtrudeArrow(box, camera);
        }

        public void RenderPlacementRect(IRenderFrameData frameData, int x0, int y0, int x1, int y1, int viewportWidth, int viewportHeight)
        {
            if (!TrySetFrame(frameData)) return;
            EnsureResources();
            float nx0 = Math.Min(x0, x1) / (float)viewportWidth  * 2f - 1f;
            float nx1 = Math.Max(x0, x1) / (float)viewportWidth  * 2f - 1f;
            float ny0 = 1f - Math.Max(y0, y1) / (float)viewportHeight * 2f;
            float ny1 = 1f - Math.Min(y0, y1) / (float)viewportHeight * 2f;
            Renderer.UpdateBuffer(ref _placementFillBuffer, new[]
            {
                nx0,ny0,0f, nx1,ny0,0f, nx1,ny1,0f,
                nx0,ny0,0f, nx1,ny1,0f, nx0,ny1,0f,
            });
            Renderer.Draw(_placementFillBuffer, 6, MTLPrimitiveType.Triangle,
                Matrix4.Identity, new Vector4(0f, 0.78f, 1f, 0.12f), depthTest: false);

            Renderer.UpdateBuffer(ref _placementLineBuffer, new[]
            {
                nx0,ny0,0f, nx1,ny0,0f, nx1,ny0,0f, nx1,ny1,0f,
                nx1,ny1,0f, nx0,ny1,0f, nx0,ny1,0f, nx0,ny0,0f,
            });
            Renderer.Draw(_placementLineBuffer, 8, MTLPrimitiveType.Line,
                Matrix4.Identity, new Vector4(0f, 0.82f, 1f, 0.9f), depthTest: false);
        }

        public override void Dispose()
        {
            MetalPrimitiveRenderer.Release(ref _edgeBuffer);
            MetalPrimitiveRenderer.Release(ref _faceBuffer);
            MetalPrimitiveRenderer.Release(ref _placementFillBuffer);
            MetalPrimitiveRenderer.Release(ref _placementLineBuffer);
            MetalPrimitiveRenderer.Release(ref _dynamicBuffer);
            base.Dispose();
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (_edgeBuffer.NativePtr == IntPtr.Zero) _edgeBuffer = Renderer.CreateStaticBuffer(Edges);
            if (_faceBuffer.NativePtr == IntPtr.Zero) _faceBuffer = Renderer.CreateStaticBuffer(Faces);
        }

        private void RenderFaceArrows(BoxSelectionTool box, OrbitCamera camera)
        {
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            float arrowLength = MathF.Max(box.ArrowWorldLength, 0.01f);

            for (int i = 0; i < 6; i++)
            {
                Vector3 face = box.HandleWorldPosition(i);
                Vector3 dir = invRot * BoxSelectionTool.HandleLocalPos[i];
                Vector3 tip = face + dir * arrowLength;

                var (fx, fy, fb) = camera.WorldToScreen(face);
                var (tx, ty, tb) = camera.WorldToScreen(tip);
                if (fb || tb) continue;

                var (fnx, fny) = ScreenToNdc(fx, fy, camera.ViewportWidth, camera.ViewportHeight);
                var (tnx, tny) = ScreenToNdc(tx, ty, camera.ViewportWidth, camera.ViewportHeight);
                int axis = i / 2;
                GripDescriptor grip = box.GetGrip(i);
                GripVisualDescriptor style = GripVisualStyleResolver.ResolveAxisGrip(
                    grip,
                    i == box.HoveredHandle,
                    box.IsFlat,
                    AxisColor[axis],
                    i == box.ActiveHandle);
                Vector4 color = style.Color;

                DrawLine(fnx, fny, tnx, tny, color);
                DrawDiamond(fnx, fny, 4f / camera.ViewportWidth, 4f / camera.ViewportHeight, color);
                DrawArrowHead(tnx, tny, fnx, fny, 0.013f, color);
            }
        }

        private void RenderCornerHandles(BoxSelectionTool box, OrbitCamera camera)
        {
            for (int i = 6; i <= 14; i++)
            {
                var (sx, sy, behind) = camera.WorldToScreen(box.HandleWorldPosition(i));
                if (behind) continue;

                var (nx, ny) = ScreenToNdc(sx, sy, camera.ViewportWidth, camera.ViewportHeight);
                GripDescriptor grip = box.GetGrip(i);
                GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(
                    grip,
                    i == box.HoveredHandle,
                    i == box.ActiveHandle);
                Vector4 color = style.Color;
                DrawDiamond(nx, ny, 12f / camera.ViewportWidth, 12f / camera.ViewportHeight, color);
            }
        }

        private void RenderRings(BoxSelectionTool box, OrbitCamera camera)
        {
            const int segmentCount = 64;
            float radius = box.RingRadius;
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));

            for (int axis = 0; axis < 3; axis++)
            {
                int write = 0;
                float prevX = 0f, prevY = 0f;
                bool prevOk = false;
                for (int j = 0; j <= segmentCount; j++)
                {
                    float t = j * MathF.Tau / segmentCount;
                    float c = MathF.Cos(t);
                    float s = MathF.Sin(t);
                    Vector3 local = axis switch
                    {
                        0 => new Vector3(0f, c, s),
                        1 => new Vector3(c, 0f, s),
                        _ => new Vector3(c, s, 0f),
                    } * radius;

                    var (sx, sy, behind) = camera.WorldToScreen(box.Center + invRot * local);
                    var (nx, ny) = ScreenToNdc(sx, sy, camera.ViewportWidth, camera.ViewportHeight);
                    if (prevOk && !behind)
                    {
                        _ringBuf[write++] = prevX; _ringBuf[write++] = prevY; _ringBuf[write++] = 0f;
                        _ringBuf[write++] = nx;    _ringBuf[write++] = ny;    _ringBuf[write++] = 0f;
                    }
                    prevX = nx;
                    prevY = ny;
                    prevOk = !behind;
                }
                if (write == 0) continue;
                bool hovered = box.HoveredHandle == 15 + axis;
                bool active = box.ActiveHandle == 15 + axis;
                GripVisualDescriptor style = GripVisualStyleResolver.ResolveRing(hovered, AxisColor[axis], active);
                Vector4 color = style.Color;
                DrawDynamic(_ringBuf, write / 3, MTLPrimitiveType.Line, color);
            }
        }

        private void RenderExtrudeArrow(BoxSelectionTool box, OrbitCamera camera)
        {
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(box.Rotation));
            Vector3 worldZ = invRot * Vector3.UnitZ;
            Vector3 face = box.HandleWorldPosition(BoxSelectionTool.ExtrudeHandle);
            Vector3 tip = face + worldZ * MathF.Max(box.HalfExtents.X, box.HalfExtents.Y) * 0.55f;
            var (fx, fy, fb) = camera.WorldToScreen(face);
            var (tx, ty, tb) = camera.WorldToScreen(tip);
            if (fb || tb) return;

            var (fnx, fny) = ScreenToNdc(fx, fy, camera.ViewportWidth, camera.ViewportHeight);
            var (tnx, tny) = ScreenToNdc(tx, ty, camera.ViewportWidth, camera.ViewportHeight);
            GripDescriptor grip = box.GetGrip(BoxSelectionTool.ExtrudeHandle);
            GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(
                grip,
                box.HoveredHandle == grip.Index,
                box.ActiveHandle == grip.Index);
            DrawLine(fnx, fny, tnx, tny, style.Color);
            DrawArrowHead(tnx, tny, fnx, fny, 0.02f, style.Color with { W = 1f });
        }

        private void DrawLine(float x0, float y0, float x1, float y1, Vector4 color)
        {
            _lineBuf[0] = x0; _lineBuf[1] = y0; _lineBuf[2] = 0f;
            _lineBuf[3] = x1; _lineBuf[4] = y1; _lineBuf[5] = 0f;
            DrawDynamic(_lineBuf, 2, MTLPrimitiveType.Line, color);
        }

        private void DrawArrowHead(float tipX, float tipY, float fromX, float fromY, float size, Vector4 color)
        {
            float dx = tipX - fromX;
            float dy = tipY - fromY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float nx = dx / len;
            float ny = dy / len;
            float px = -ny;
            float py = nx;

            _arrowBuf[0] = tipX; _arrowBuf[1] = tipY; _arrowBuf[2] = 0f;
            _arrowBuf[3] = tipX - nx * size * 2f + px * size; _arrowBuf[4] = tipY - ny * size * 2f + py * size; _arrowBuf[5] = 0f;
            _arrowBuf[6] = tipX - nx * size * 2f - px * size; _arrowBuf[7] = tipY - ny * size * 2f - py * size; _arrowBuf[8] = 0f;
            DrawDynamic(_arrowBuf, 3, MTLPrimitiveType.Triangle, color);
        }

        private void DrawDiamond(float x, float y, float sx, float sy, Vector4 color)
        {
            _diamondBuf[0] = x; _diamondBuf[1] = y + sy; _diamondBuf[2] = 0f;
            _diamondBuf[3] = x + sx; _diamondBuf[4] = y; _diamondBuf[5] = 0f;
            _diamondBuf[6] = x; _diamondBuf[7] = y - sy; _diamondBuf[8] = 0f;
            _diamondBuf[9] = x; _diamondBuf[10] = y + sy; _diamondBuf[11] = 0f;
            _diamondBuf[12] = x; _diamondBuf[13] = y - sy; _diamondBuf[14] = 0f;
            _diamondBuf[15] = x - sx; _diamondBuf[16] = y; _diamondBuf[17] = 0f;
            DrawDynamic(_diamondBuf, 6, MTLPrimitiveType.Triangle, color);
        }

        private void DrawDynamic(float[] vertices, int vertexCount, MTLPrimitiveType primitive, Vector4 color)
        {
            Renderer.UpdateBuffer(ref _dynamicBuffer, vertices);
            Renderer.Draw(_dynamicBuffer, vertexCount, primitive, Matrix4.Identity, color, depthTest: false);
        }

        private static (float x, float y) ScreenToNdc(float sx, float sy, float width, float height)
            => (sx / width * 2f - 1f, 1f - sy / height * 2f);
    }

    [SupportedOSPlatform("macos")]
    internal sealed class MetalSphereGizmoRenderer : MetalGizmoRendererBase
    {
        private MTLBuffer _circleBuffer;
        private int _circleVertexCount;

        public override void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            if (!TrySetFrame(frameData)) return;
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

        public override void Dispose()
        {
            MetalPrimitiveRenderer.Release(ref _circleBuffer);
            base.Dispose();
        }
    }

    [SupportedOSPlatform("macos")]
    internal sealed class MetalCylinderGizmoRenderer : MetalGizmoRendererBase
    {
        private MTLBuffer _wireBuffer;
        private int _wireVertexCount;

        public override void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            if (!TrySetFrame(frameData)) return;
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

        public override void Dispose()
        {
            MetalPrimitiveRenderer.Release(ref _wireBuffer);
            base.Dispose();
        }
    }
}
