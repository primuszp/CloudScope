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

        protected static (float x, float y) ScreenToNdc(float sx, float sy, float width, float height)
            => (sx / width * 2f - 1f, 1f - sy / height * 2f);

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

    }

    [SupportedOSPlatform("macos")]
    internal sealed class MetalSphereGizmoRenderer : MetalGizmoRendererBase
    {
        private const int Seg = 64;
        private const int Lat = 16;
        private const int Lon = 32;

        private MTLBuffer _fillBuffer;
        private MTLBuffer _circleBuffer;
        private MTLBuffer _dynamicBuffer;
        private readonly float[] _diamondBuf = new float[18];
        private int _fillVertexCount;
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
            Renderer.Draw(_fillBuffer, _fillVertexCount, MTLPrimitiveType.Triangle, mvp,
                new Vector4(0.30f, 0.60f, 0.95f, 0.07f), depthTest: true);
            RenderAxis(mvp);
            Renderer.Draw(_circleBuffer, _circleVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.85f), depthTest: true);
            Renderer.Draw(_circleBuffer, _circleVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.18f), depthTest: false);
            RenderHandles(sphere, camera);
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (_circleBuffer.NativePtr != IntPtr.Zero) return;

            _fillVertexCount = Lat * Lon * 6;
            float[] fill = new float[_fillVertexCount * 3];
            int fi = 0;
            for (int la = 0; la < Lat; la++)
            {
                float phi0 = MathF.PI * la / Lat - MathF.PI * 0.5f;
                float phi1 = MathF.PI * (la + 1) / Lat - MathF.PI * 0.5f;
                float cp0 = MathF.Cos(phi0), sp0 = MathF.Sin(phi0);
                float cp1 = MathF.Cos(phi1), sp1 = MathF.Sin(phi1);
                for (int lo = 0; lo < Lon; lo++)
                {
                    float th0 = MathF.Tau * lo / Lon;
                    float th1 = MathF.Tau * (lo + 1) / Lon;
                    float ct0 = MathF.Cos(th0), st0 = MathF.Sin(th0);
                    float ct1 = MathF.Cos(th1), st1 = MathF.Sin(th1);
                    AddSpherePoint(fill, ref fi, cp0, sp0, ct0, st0);
                    AddSpherePoint(fill, ref fi, cp1, sp1, ct0, st0);
                    AddSpherePoint(fill, ref fi, cp1, sp1, ct1, st1);
                    AddSpherePoint(fill, ref fi, cp0, sp0, ct0, st0);
                    AddSpherePoint(fill, ref fi, cp1, sp1, ct1, st1);
                    AddSpherePoint(fill, ref fi, cp0, sp0, ct1, st1);
                }
            }
            _fillBuffer = Renderer.CreateStaticBuffer(fill);

            float[] data = new float[3 * Seg * 2 * 3];
            int i = 0;
            AddCircle(data, ref i, Seg, 0);
            AddCircle(data, ref i, Seg, 1);
            AddCircle(data, ref i, Seg, 2);
            _circleVertexCount = i / 3;
            _circleBuffer      = Renderer.CreateStaticBuffer(data);
        }

        private void RenderHandles(SphereSelectionTool sphere, OrbitCamera camera)
        {
            foreach (GripDescriptor grip in sphere.Grips)
            {
                int i = grip.Index;
                var (sx, sy, behind) = camera.WorldToScreen(sphere.HandleWorldPosition(i));
                if (behind) continue;

                var (nx, ny) = ScreenToNdc(sx, sy, camera.ViewportWidth, camera.ViewportHeight);
                GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(
                    grip,
                    i == sphere.HoveredHandle,
                    i == sphere.ActiveHandle);
                DrawDiamond(nx, ny, 12f / camera.ViewportWidth, 12f / camera.ViewportHeight, style.Color);
            }
        }

        private void DrawDiamond(float x, float y, float sx, float sy, Vector4 color)
        {
            _diamondBuf[0] = x; _diamondBuf[1] = y + sy; _diamondBuf[2] = 0f;
            _diamondBuf[3] = x + sx; _diamondBuf[4] = y; _diamondBuf[5] = 0f;
            _diamondBuf[6] = x; _diamondBuf[7] = y - sy; _diamondBuf[8] = 0f;
            _diamondBuf[9] = x; _diamondBuf[10] = y + sy; _diamondBuf[11] = 0f;
            _diamondBuf[12] = x; _diamondBuf[13] = y - sy; _diamondBuf[14] = 0f;
            _diamondBuf[15] = x - sx; _diamondBuf[16] = y; _diamondBuf[17] = 0f;
            Renderer.UpdateBuffer(ref _dynamicBuffer, _diamondBuf);
            Renderer.Draw(_dynamicBuffer, 6, MTLPrimitiveType.Triangle, Matrix4.Identity, color, depthTest: false);
        }

        private static void AddSpherePoint(float[] data, ref int i, float cp, float sp, float ct, float st)
        {
            data[i++] = cp * ct;
            data[i++] = sp;
            data[i++] = cp * st;
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
            MetalPrimitiveRenderer.Release(ref _fillBuffer);
            MetalPrimitiveRenderer.Release(ref _circleBuffer);
            MetalPrimitiveRenderer.Release(ref _dynamicBuffer);
            base.Dispose();
        }
    }

    [SupportedOSPlatform("macos")]
    internal sealed class MetalCylinderGizmoRenderer : MetalGizmoRendererBase
    {
        private const int CapSeg = 64;
        private const int LatSeg = 12;

        private MTLBuffer _fillBuffer;
        private MTLBuffer _wireBuffer;
        private MTLBuffer _dynamicBuffer;
        private readonly float[] _lineBuf = new float[6];
        private readonly float[] _arrowBuf = new float[9];
        private readonly float[] _diamondBuf = new float[18];
        private float[] _ringBuf = new float[64 * 6];
        private int _fillVertexCount;
        private int _wireVertexCount;

        public override void Render(IRenderFrameData frameData, ISelectionTool tool, Matrix4 view, Matrix4 proj, OrbitCamera camera)
        {
            if (!TrySetFrame(frameData)) return;
            var cyl = (CylinderSelectionTool)tool;
            EnsureResources();

            if (cyl.Phase == ToolPhase.Drawing)
            {
                RenderDrawingCircle(cyl, camera);
                return;
            }

            if (cyl.Radius < 1e-5f || cyl.HalfHeight < 1e-5f) return;

            Matrix4 model = Matrix4.CreateScale(cyl.Radius, cyl.Radius, cyl.HalfHeight)
                          * Matrix4.CreateFromQuaternion(cyl.Rotation)
                          * Matrix4.CreateTranslation(cyl.Center);
            Matrix4 mvp = model * view * proj;
            Renderer.Draw(_fillBuffer, _fillVertexCount, MTLPrimitiveType.Triangle, mvp,
                new Vector4(0.30f, 0.60f, 0.95f, 0.07f), depthTest: true);
            RenderAxis(mvp);
            Renderer.Draw(_wireBuffer, _wireVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.85f), depthTest: true);
            Renderer.Draw(_wireBuffer, _wireVertexCount, MTLPrimitiveType.Line, mvp,
                new Vector4(0.25f, 0.85f, 0.95f, 0.18f), depthTest: false);
            RenderExtrudeArrow(cyl, camera);
            RenderRings(cyl, camera);
            RenderHandles(cyl, camera);
        }

        private void EnsureResources()
        {
            EnsureBase();
            if (_wireBuffer.NativePtr != IntPtr.Zero) return;

            int sideVerts = LatSeg * CapSeg * 6;
            int capVerts = CapSeg * 3 * 2;
            _fillVertexCount = sideVerts + capVerts;
            float[] fill = new float[_fillVertexCount * 3];
            int fi = 0;
            float step = MathF.Tau / CapSeg;
            for (int la = 0; la < LatSeg; la++)
            {
                float z0 = -1f + 2f * la / LatSeg;
                float z1 = -1f + 2f * (la + 1) / LatSeg;
                for (int s = 0; s < CapSeg; s++)
                {
                    float a0 = s * step, a1 = (s + 1) * step;
                    float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                    float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                    AddPt(fill, ref fi, c0, s0, z0); AddPt(fill, ref fi, c1, s1, z0); AddPt(fill, ref fi, c1, s1, z1);
                    AddPt(fill, ref fi, c0, s0, z0); AddPt(fill, ref fi, c1, s1, z1); AddPt(fill, ref fi, c0, s0, z1);
                }
            }
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                AddPt(fill, ref fi, 0f, 0f, 1f);
                AddPt(fill, ref fi, MathF.Cos(a0), MathF.Sin(a0), 1f);
                AddPt(fill, ref fi, MathF.Cos(a1), MathF.Sin(a1), 1f);
            }
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step;
                AddPt(fill, ref fi, 0f, 0f, -1f);
                AddPt(fill, ref fi, MathF.Cos(a1), MathF.Sin(a1), -1f);
                AddPt(fill, ref fi, MathF.Cos(a0), MathF.Sin(a0), -1f);
            }
            _fillBuffer = Renderer.CreateStaticBuffer(fill);

            float[] data = new float[(CapSeg * 4 + 8) * 3];
            int i = 0;
            for (int s = 0; s < CapSeg; s++)
            {
                float a0 = s * MathF.Tau / CapSeg;
                float a1 = (s + 1) * MathF.Tau / CapSeg;
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

        private void RenderDrawingCircle(CylinderSelectionTool cyl, OrbitCamera camera)
        {
            if (cyl.Radius < 1e-5f) return;
            var (cx, cy, centerBehind) = camera.WorldToScreen(cyl.Center);
            if (centerBehind) return;

            const int n = 64;
            int write = 0;
            float prevX = 0f, prevY = 0f;
            bool prevOk = false;
            for (int j = 0; j <= n; j++)
            {
                float t = j * MathF.Tau / n;
                Vector3 pt = cyl.Center + new Vector3(MathF.Cos(t) * cyl.Radius, MathF.Sin(t) * cyl.Radius, 0f);
                var (sx, sy, behind) = camera.WorldToScreen(pt);
                var (nx, ny) = ScreenToNdc(sx, sy, camera.ViewportWidth, camera.ViewportHeight);
                if (prevOk && !behind)
                {
                    EnsureRingCapacity(write + 6);
                    _ringBuf[write++] = prevX; _ringBuf[write++] = prevY; _ringBuf[write++] = 0f;
                    _ringBuf[write++] = nx; _ringBuf[write++] = ny; _ringBuf[write++] = 0f;
                }
                prevX = nx; prevY = ny; prevOk = !behind;
            }
            if (write == 0) return;

            DrawDynamic(_ringBuf, write / 3, MTLPrimitiveType.Line, new Vector4(0.25f, 0.85f, 0.95f, 0.90f));
            var (cnx, cny) = ScreenToNdc(cx, cy, camera.ViewportWidth, camera.ViewportHeight);
            DrawDiamond(cnx, cny, 6f / camera.ViewportWidth, 6f / camera.ViewportHeight, new Vector4(0.3f, 1f, 0.45f, 0.9f));
        }

        private void RenderExtrudeArrow(CylinderSelectionTool cyl, OrbitCamera camera)
        {
            Vector3 top = cyl.HandleWorldPosition(1);
            Vector3 tip = top + cyl.Axis * MathF.Max(cyl.Radius * 0.55f, 0.05f);
            var (fx, fy, fb) = camera.WorldToScreen(top);
            var (tx, ty, tb) = camera.WorldToScreen(tip);
            if (fb || tb) return;

            var (fnx, fny) = ScreenToNdc(fx, fy, camera.ViewportWidth, camera.ViewportHeight);
            var (tnx, tny) = ScreenToNdc(tx, ty, camera.ViewportWidth, camera.ViewportHeight);
            GripDescriptor grip = cyl.GetGrip(1);
            bool hovered = cyl.HoveredHandle >= 0 && cyl.GetGrip(cyl.HoveredHandle).IsPrimary;
            GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(grip, hovered, cyl.ActiveHandle == grip.Index);
            DrawLine(fnx, fny, tnx, tny, style.Color);
            DrawArrowHead(tnx, tny, fnx, fny, 0.02f, style.Color with { W = 1f });
        }

        private void RenderRings(CylinderSelectionTool cyl, OrbitCamera camera)
        {
            const int n = 64;
            float radius = cyl.RingRadius;
            Matrix3 invRot = Matrix3.Transpose(Matrix3.CreateFromQuaternion(cyl.Rotation));
            for (int axis = 0; axis < 3; axis++)
            {
                int write = 0;
                float prevX = 0f, prevY = 0f;
                bool prevOk = false;
                for (int j = 0; j <= n; j++)
                {
                    float t = j * MathF.Tau / n;
                    float ct = MathF.Cos(t), st = MathF.Sin(t);
                    Vector3 local = axis switch
                    {
                        0 => new Vector3(0f, ct, st),
                        1 => new Vector3(ct, 0f, st),
                        _ => new Vector3(ct, st, 0f),
                    } * radius;
                    var (sx, sy, behind) = camera.WorldToScreen(cyl.Center + invRot * local);
                    var (nx, ny) = ScreenToNdc(sx, sy, camera.ViewportWidth, camera.ViewportHeight);
                    if (prevOk && !behind)
                    {
                        EnsureRingCapacity(write + 6);
                        _ringBuf[write++] = prevX; _ringBuf[write++] = prevY; _ringBuf[write++] = 0f;
                        _ringBuf[write++] = nx; _ringBuf[write++] = ny; _ringBuf[write++] = 0f;
                    }
                    prevX = nx; prevY = ny; prevOk = !behind;
                }
                if (write == 0) continue;

                bool hovered = cyl.HoveredHandle >= 0
                    && cyl.GetGrip(cyl.HoveredHandle).Kind == GripKind.RotationRing
                    && cyl.GetGrip(cyl.HoveredHandle).Axis == axis;
                bool active = cyl.ActiveHandle >= 0
                    && cyl.GetGrip(cyl.ActiveHandle).Kind == GripKind.RotationRing
                    && cyl.GetGrip(cyl.ActiveHandle).Axis == axis;
                GripVisualDescriptor style = GripVisualStyleResolver.ResolveRing(hovered, AxisColor[axis], active);
                DrawDynamic(_ringBuf, write / 3, MTLPrimitiveType.Line, style.Color);
            }
        }

        private void RenderHandles(CylinderSelectionTool cyl, OrbitCamera camera)
        {
            foreach (GripDescriptor grip in cyl.Grips)
            {
                if (grip.Kind == GripKind.RotationRing) continue;
                int i = grip.Index;
                var (sx, sy, behind) = camera.WorldToScreen(cyl.HandleWorldPosition(i));
                if (behind) continue;
                var (nx, ny) = ScreenToNdc(sx, sy, camera.ViewportWidth, camera.ViewportHeight);
                GripVisualDescriptor style = GripVisualStyleResolver.ResolvePointGrip(grip, i == cyl.HoveredHandle, i == cyl.ActiveHandle);
                DrawDiamond(nx, ny, 12f / camera.ViewportWidth, 12f / camera.ViewportHeight, style.Color);
            }
        }

        private void DrawLine(float x0, float y0, float x1, float y1, Vector4 color)
        {
            _lineBuf[0] = x0; _lineBuf[1] = y0; _lineBuf[2] = 0f;
            _lineBuf[3] = x1; _lineBuf[4] = y1; _lineBuf[5] = 0f;
            DrawDynamic(_lineBuf, 2, MTLPrimitiveType.Line, color);
        }

        private void DrawArrowHead(float tipX, float tipY, float fromX, float fromY, float size, Vector4 color)
        {
            float dx = tipX - fromX, dy = tipY - fromY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float nx = dx / len, ny = dy / len, px = -ny, py = nx;
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

        private void EnsureRingCapacity(int requested)
        {
            if (requested <= _ringBuf.Length) return;
            Array.Resize(ref _ringBuf, Math.Max(requested, _ringBuf.Length * 2));
        }

        private static void AddPt(float[] data, ref int i, float x, float y, float z)
        { data[i++] = x; data[i++] = y; data[i++] = z; }

        public override void Dispose()
        {
            MetalPrimitiveRenderer.Release(ref _fillBuffer);
            MetalPrimitiveRenderer.Release(ref _wireBuffer);
            MetalPrimitiveRenderer.Release(ref _dynamicBuffer);
            base.Dispose();
        }
    }

}
