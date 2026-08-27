using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpMetal.Metal;
using OpenTK.Mathematics;

namespace CloudScope.Platform.Metal
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MetalPointUniforms
    {
        private readonly Matrix4 view;
        private readonly Matrix4 projection;
        private readonly Vector4 point;

        /// <summary>Multiplied into the point's own color; white leaves it unchanged.</summary>
        private readonly Vector4 tint;

        public MetalPointUniforms(
            Matrix4 view, Matrix4 projection, float pointSize, float alpha = 1f, Vector3? layerTint = null)
        {
            this.view = view;
            this.projection = projection;
            point = new Vector4(pointSize, alpha, 0f, 0f);
            Vector3 rgb = layerTint ?? Vector3.One;
            tint = new Vector4(rgb.X, rgb.Y, rgb.Z, 1f);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MetalAttributePointUniforms
    {
        private readonly Matrix4 view;
        private readonly Matrix4 projection;
        private readonly Vector4 point;

        /// <summary>Multiplied into the point's own color; white leaves it unchanged.</summary>
        private readonly Vector4 tint;

        public MetalAttributePointUniforms(
            Matrix4 view, Matrix4 projection, float pointSize, int colorSource, Vector3? layerTint = null)
        {
            this.view = view;
            this.projection = projection;
            point = new Vector4(pointSize, colorSource, 0f, 0f);
            Vector3 rgb = layerTint ?? Vector3.One;
            tint = new Vector4(rgb.X, rgb.Y, rgb.Z, 1f);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MetalPaletteColor
    {
        private readonly float r;
        private readonly float g;
        private readonly float b;

        public MetalPaletteColor(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MetalColorUniforms
    {
        private readonly Matrix4 mvp;
        private readonly Vector4 color;
        private readonly Vector4 line;

        /// <param name="line">
        /// Wide-line parameters: viewport width and height in pixels, then the line width.
        /// The plain color shaders ignore the field.
        /// </param>
        public MetalColorUniforms(Matrix4 mvp, Vector4 color, Vector4 line = default)
        {
            this.mvp = mvp;
            this.color = color;
            this.line = line;
        }
    }

    [SupportedOSPlatform("macos")]
    internal static unsafe class MetalBufferWriter
    {
        public static void Write<T>(MTLBuffer buffer, T value) where T : unmanaged
        {
            *(T*)buffer.Contents.ToPointer() = value;
            // StorageModeShared: no DidModifyRange needed
        }
    }

    [SupportedOSPlatform("macos")]
    internal static class MetalShaderLibrary
    {
        private const string PivotPointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct PointVertex { packed_float3 position; packed_float3 color; };
struct PointUniforms { float4x4 view; float4x4 projection; float4 point; float4 tint; };
struct VertexOut { float4 position [[position]]; float point_size [[point_size]]; float alpha; };

vertex VertexOut pivot_point_vertex(
    uint vertexId [[vertex_id]],
    const device PointVertex* points [[buffer(0)]],
    constant PointUniforms& uniforms [[buffer(1)]])
{
    VertexOut out;
    out.position = uniforms.projection * uniforms.view * float4(points[vertexId].position, 1.0);
    out.point_size = uniforms.point.x;
    out.alpha = uniforms.point.y;
    return out;
}

fragment float4 pivot_point_fragment(VertexOut in [[stage_in]], float2 pointCoord [[point_coord]])
{
    float2 p = pointCoord * 2.0 - 1.0;
    float radiusSquared = dot(p, p);
    if (radiusSquared > 1.0) discard_fragment();
    float edge = smoothstep(0.85, 1.0, sqrt(radiusSquared));
    float z = sqrt(1.0 - radiusSquared);
    float3 normal = float3(p.x, -p.y, z);
    float diffuse = max(dot(normal, normalize(float3(1.0, 1.5, 1.0))), 0.25);
    float3 core = float3(1.0, 0.92, 0.2) * diffuse;
    float3 glow = float3(1.0, 0.7, 0.0);
    return float4(mix(core, glow, edge), in.alpha);
}";

        /// <summary>
        /// Highlight and preview points, which are handed to the GPU as the CPU-side
        /// <c>PointData</c> with float colors.
        /// </summary>
        private const string PointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct PointVertex { packed_float3 position; packed_float3 color; };
struct PointUniforms { float4x4 view; float4x4 projection; float4 point; float4 tint; };
struct VertexOut { float4 position [[position]]; float point_size [[point_size]]; float3 color; };

vertex VertexOut point_vertex(
    uint vertexId [[vertex_id]],
    const device PointVertex* points [[buffer(0)]],
    constant PointUniforms& uniforms [[buffer(1)]])
{
    PointVertex p = points[vertexId];
    VertexOut out;
    out.position = uniforms.projection * uniforms.view * float4(p.position, 1.0);
    out.point_size = uniforms.point.x;
    out.color = p.color;
    return out;
}

fragment float4 point_fragment(VertexOut in [[stage_in]])
{
    return float4(in.color, 1.0);
}";

        /// <summary>
        /// The point cloud itself, reading the packed sixteen-byte vertex. Must stay in step
        /// with <c>GpuPointVertex</c>.
        /// </summary>
        private const string PackedPointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct PackedPointVertex { packed_float3 position; uchar4 color; };
struct PointUniforms { float4x4 view; float4x4 projection; float4 point; float4 tint; };
struct VertexOut { float4 position [[position]]; float point_size [[point_size]]; float3 color; };

vertex VertexOut packed_point_vertex(
    uint vertexId [[vertex_id]],
    const device PackedPointVertex* points [[buffer(0)]],
    constant PointUniforms& uniforms [[buffer(1)]])
{
    PackedPointVertex p = points[vertexId];
    VertexOut out;
    out.position = uniforms.projection * uniforms.view * float4(p.position, 1.0);
    out.point_size = uniforms.point.x;
    out.color = float3(p.color.rgb) / 255.0 * uniforms.tint.rgb;
    return out;
}

fragment float4 packed_point_fragment(VertexOut in [[stage_in]])
{
    return float4(in.color, 1.0);
}";

        private const string AttributePointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

// Must stay in step with GpuPointVertex and GpuPointAttribute.
struct PackedPointVertex { packed_float3 position; uchar4 color; };
struct PointAttributes
{
    ushort zNormalized;
    ushort intensityNormalized;
    uchar4 color;
    uchar classCode;
    uchar returnNumber;
    ushort padding;
};
struct PaletteColor { float r; float g; float b; };
struct AttributePointUniforms { float4x4 view; float4x4 projection; float4 point; float4 tint; };
struct VertexOut { float4 position [[position]]; float point_size [[point_size]]; float3 color; };

float3 paletteColor(const device PaletteColor* palette, uint index)
{
    PaletteColor c = palette[index];
    return float3(c.r, c.g, c.b);
}

float3 gradientColor(float t)
{
    t = clamp(t, 0.0f, 1.0f);
    return float3(t, min(1.0f, 2.0f * min(t, 1.0f - t)), 1.0f - t);
}

float3 heightColor(float z)
{
    z = clamp(z, 0.0f, 1.0f);
    return float3(z, 1.0f - abs(2.0f * z - 1.0f), 1.0f - z);
}

vertex VertexOut attribute_point_vertex(
    uint vertexId [[vertex_id]],
    const device PackedPointVertex* points [[buffer(0)]],
    constant AttributePointUniforms& uniforms [[buffer(1)]],
    const device PointAttributes* attributes [[buffer(2)]],
    const device PaletteColor* classPalette [[buffer(3)]])
{
    PackedPointVertex p = points[vertexId];
    PointAttributes a = attributes[vertexId];
    int colorSource = int(uniforms.point.y);

    VertexOut out;
    out.position = uniforms.projection * uniforms.view * float4(p.position, 1.0);
    out.point_size = uniforms.point.x;
    if (colorSource == 0)
        out.color = float3(a.color.rgb) / 255.0;
    else if (colorSource == 1)
        out.color = heightColor(float(a.zNormalized) / 65535.0);
    else if (colorSource == 2)
        out.color = paletteColor(classPalette, uint(a.classCode));
    else if (colorSource == 3)
        out.color = gradientColor(float(a.intensityNormalized) / 65535.0);
    else if (colorSource == 4)
        out.color = paletteColor(classPalette, uint(a.returnNumber));
    else
        out.color = float3(p.color.rgb) / 255.0;

    // White leaves a cloud exactly as it was stored; a layer only tints when asked to.
    out.color *= uniforms.tint.rgb;
    return out;
}

fragment float4 attribute_point_fragment(VertexOut in [[stage_in]])
{
    return float4(in.color, 1.0);
}";

        private const string ColorShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct ColorUniforms { float4x4 mvp; float4 color; };
struct ColorVertexOut { float4 position [[position]]; };

vertex ColorVertexOut color_vertex(
    uint vertexId [[vertex_id]],
    const device packed_float3* vertices [[buffer(0)]],
    constant ColorUniforms& uniforms [[buffer(1)]])
{
    ColorVertexOut out;
    out.position = uniforms.mvp * float4(float3(vertices[vertexId]), 1.0);
    return out;
}

fragment float4 color_fragment(ColorVertexOut in [[stage_in]], constant ColorUniforms& uniforms [[buffer(1)]])
{
    return uniforms.color;
}";

        public static MTLRenderPipelineState CreatePointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, PointShaderSource, "point_vertex", "point_fragment",
                colorFormat, depthFormat, blend: false, sampleCount);

        public static MTLRenderPipelineState CreatePackedPointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, PackedPointShaderSource, "packed_point_vertex", "packed_point_fragment",
                colorFormat, depthFormat, blend: false, sampleCount);

        public static MTLRenderPipelineState CreateAttributePointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, AttributePointShaderSource, "attribute_point_vertex", "attribute_point_fragment",
                colorFormat, depthFormat, blend: false, sampleCount);

        private const string WideLineShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct ColorUniforms { float4x4 mvp; float4 color; float4 line; };
struct ColorVertexOut { float4 position [[position]]; float side [[user(locn0)]]; };

// One instance per segment, four vertices per instance: the segment is expanded into a
// screen-space quad because Metal has no line width. Matches the OpenGL wide-line shader.
vertex ColorVertexOut wide_line_vertex(
    uint vertexId [[vertex_id]],
    uint instanceId [[instance_id]],
    const device packed_float3* vertices [[buffer(0)]],
    constant ColorUniforms& uniforms [[buffer(1)]])
{
    float4 clipStart = uniforms.mvp * float4(float3(vertices[instanceId * 2u]), 1.0);
    float4 clipEnd   = uniforms.mvp * float4(float3(vertices[instanceId * 2u + 1u]), 1.0);

    bool atStart = vertexId < 2u;
    float4 clipHere  = atStart ? clipStart : clipEnd;
    float4 clipThere = atStart ? clipEnd   : clipStart;
    float side = (vertexId % 2u == 0u) ? -1.0 : 1.0;

    ColorVertexOut out;
    if (clipHere.w <= 0.0 || clipThere.w <= 0.0)
    {
        out.position = clipHere;
        out.side = 0.0;
        return out;
    }

    float2 halfViewport = max(uniforms.line.xy, float2(1.0)) * 0.5;
    float2 screenHere  = clipHere.xy  / clipHere.w  * halfViewport;
    float2 screenThere = clipThere.xy / clipThere.w * halfViewport;

    float2 delta = screenThere - screenHere;
    float length2 = dot(delta, delta);
    float2 direction = length2 > 1e-12 ? delta * rsqrt(length2) : float2(1.0, 0.0);
    float2 normal = float2(-direction.y, direction.x);

    // Do not overlap transparent line-list quads at their endpoints. Such overlap makes
    // a closed ring brighter at every join, which reads as a scalloped circumference.
    // The circle meshes are deliberately dense, so their endpoints meet without a seam.
    float2 offsetNdc = normal * side * (uniforms.line.z * 0.5) / halfViewport;
    out.position = float4(clipHere.xy + offsetNdc * clipHere.w, clipHere.z, clipHere.w);
    out.side = side;
    return out;
}

fragment float4 wide_line_fragment(ColorVertexOut in [[stage_in]], constant ColorUniforms& uniforms [[buffer(1)]])
{
    float edge = abs(in.side);
    float feather = max(fwidth(edge), 0.001);
    float coverage = 1.0 - smoothstep(1.0 - feather, 1.0 + feather, edge);
    return float4(uniforms.color.rgb, uniforms.color.a * coverage);
}";

        public static MTLRenderPipelineState CreateWideLinePipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, WideLineShaderSource, "wide_line_vertex", "wide_line_fragment",
                colorFormat, depthFormat, blend: true, sampleCount);

        private const string SmoothPolylineShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct ColorUniforms { float4x4 mvp; float4 color; float4 line; };
struct PolylineVertex { packed_float3 previous; packed_float3 current; packed_float3 next; };
struct ColorVertexOut { float4 position [[position]]; float side [[user(locn0)]]; };

float2 direction_or(float2 value, float2 fallback)
{
    float length2 = dot(value, value);
    return length2 > 1e-12 ? value * rsqrt(length2) : fallback;
}

// A connected mitered ribbon. Every pair of input vertices shares a previous/current/next
// triplet, so a closed circle is one continuous surface rather than overlapping segments.
vertex ColorVertexOut smooth_polyline_vertex(
    uint vertexId [[vertex_id]],
    const device PolylineVertex* vertices [[buffer(0)]],
    constant ColorUniforms& uniforms [[buffer(1)]])
{
    PolylineVertex lineVertex = vertices[vertexId];
    float4 previous = uniforms.mvp * float4(float3(lineVertex.previous), 1.0);
    float4 current  = uniforms.mvp * float4(float3(lineVertex.current),  1.0);
    float4 next     = uniforms.mvp * float4(float3(lineVertex.next),     1.0);
    float side = (vertexId % 2u == 0u) ? -1.0 : 1.0;
    ColorVertexOut out;

    if (previous.w <= 0.0 || current.w <= 0.0 || next.w <= 0.0)
    {
        out.position = current;
        out.side = 0.0;
        return out;
    }

    float2 halfViewport = max(uniforms.line.xy, float2(1.0)) * 0.5;
    float2 p0 = previous.xy / previous.w * halfViewport;
    float2 p1 = current.xy  / current.w  * halfViewport;
    float2 p2 = next.xy     / next.w     * halfViewport;
    float2 incoming = direction_or(p1 - p0, float2(1.0, 0.0));
    float2 outgoing = direction_or(p2 - p1, incoming);
    float2 tangent = direction_or(incoming + outgoing, outgoing);
    float2 miter = float2(-tangent.y, tangent.x);
    float2 outgoingNormal = float2(-outgoing.y, outgoing.x);
    float miterScale = min(1.0 / max(abs(dot(miter, outgoingNormal)), 0.25), 4.0);
    float outerHalfWidth = uniforms.line.z * 0.5 + 0.5;
    float2 offsetNdc = miter * side * outerHalfWidth * miterScale / halfViewport;
    out.position = float4(current.xy + offsetNdc * current.w, current.z, current.w);
    out.side = side * outerHalfWidth;
    return out;
}

fragment float4 smooth_polyline_fragment(ColorVertexOut in [[stage_in]], constant ColorUniforms& uniforms [[buffer(1)]])
{
    float halfWidth = uniforms.line.z * 0.5;
    float coverage = 1.0 - smoothstep(halfWidth - 0.5, halfWidth + 0.5, abs(in.side));
    return float4(uniforms.color.rgb, uniforms.color.a * coverage);
}";

        public static MTLRenderPipelineState CreateSmoothPolylinePipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, SmoothPolylineShaderSource, "smooth_polyline_vertex", "smooth_polyline_fragment",
                colorFormat, depthFormat, blend: true, sampleCount);

        public static MTLRenderPipelineState CreateColorPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, ColorShaderSource, "color_vertex", "color_fragment",
                colorFormat, depthFormat, blend: true, sampleCount);

        public static MTLRenderPipelineState CreatePivotPointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, PivotPointShaderSource, "pivot_point_vertex", "pivot_point_fragment",
                colorFormat, depthFormat, blend: true, sampleCount);

        public static MTLDepthStencilState CreateDepthState(
            MTLDevice device, bool depthWrite, MTLCompareFunction compareFunction = MTLCompareFunction.LessEqual)
        {
            var desc = new MTLDepthStencilDescriptor();
            desc.DepthCompareFunction = compareFunction;
            desc.IsDepthWriteEnabled = depthWrite;
            return device.NewDepthStencilState(desc);
        }

        private static MTLRenderPipelineState CreatePipeline(
            MTLDevice device, string source, string vertFn, string fragFn,
            MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, bool blend, int sampleCount)
        {
            // Use the synchronous overload to avoid a potential deadlock when called
            // from the main thread (OnDidFinishLaunching → Load → Initialize).
            var libError = new SharpMetal.Foundation.NSError(IntPtr.Zero);
            var library  = device.NewLibrary(source, new MTLCompileOptions(IntPtr.Zero), ref libError);
            if (libError.NativePtr != IntPtr.Zero)
                throw new InvalidOperationException("Metal shader compile failed: " + libError.LocalizedDescription.ToString());

            var vert = library.NewFunction(vertFn);
            var frag = library.NewFunction(fragFn);

            var desc = new MTLRenderPipelineDescriptor();
            desc.VertexFunction = vert;
            desc.FragmentFunction = frag;
            desc.DepthAttachmentPixelFormat = depthFormat;
            desc.RasterSampleCount = (ulong)Math.Max(sampleCount, 1);

            var ca = desc.ColorAttachments.Object(0);
            ca.PixelFormat = colorFormat;
            if (blend)
            {
                ca.IsBlendingEnabled = true;
                ca.SourceRGBBlendFactor = MTLBlendFactor.SourceAlpha;
                ca.DestinationRGBBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;
                ca.SourceAlphaBlendFactor = MTLBlendFactor.One;
                ca.DestinationAlphaBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;
            }
            desc.ColorAttachments.SetObject(ca, 0);

            var error = new SharpMetal.Foundation.NSError(IntPtr.Zero);
            var pipeline = device.NewRenderPipelineState(desc, ref error);
            if (error.NativePtr != IntPtr.Zero)
                throw new InvalidOperationException("Pipeline creation failed: " + error.LocalizedDescription.ToString());

            return pipeline;
        }
    }
}
