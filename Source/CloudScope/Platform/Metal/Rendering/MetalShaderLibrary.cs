using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpMetal.Metal;
using OpenTK.Mathematics;
using CloudScope.Rendering;
using CloudScope.Sections;

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
        private readonly Vector4 sectionCenter;
        private readonly Vector4 sectionAlong;
        private readonly Vector4 sectionNormal;
        private readonly Vector4 sectionHalfSize;

        public MetalPointUniforms(
            Matrix4 view, Matrix4 projection, float pointSize, float alpha = 1f,
            Vector3? layerTint = null, SectionClip section = default)
        {
            this.view = view;
            this.projection = projection;
            point = new Vector4(pointSize, alpha, 0f, 0f);
            Vector3 rgb = layerTint ?? Vector3.One;
            tint = new Vector4(rgb.X, rgb.Y, rgb.Z, 1f);
            sectionCenter = new Vector4(section.Center, section.Enabled ? 1f : 0f);
            sectionAlong = new Vector4(section.Along, 0f);
            sectionNormal = new Vector4(section.Normal, 0f);
            sectionHalfSize = new Vector4(section.HalfLength, section.HalfWidth, 0f, 0f);
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
        private readonly Vector4 sectionCenter;
        private readonly Vector4 sectionAlong;
        private readonly Vector4 sectionNormal;
        private readonly Vector4 sectionHalfSize;

        public MetalAttributePointUniforms(
            Matrix4 view, Matrix4 projection, float pointSize, int colorSource,
            Vector3? layerTint = null, SectionClip section = default)
        {
            this.view = view;
            this.projection = projection;
            point = new Vector4(pointSize, colorSource, 0f, 0f);
            Vector3 rgb = layerTint ?? Vector3.One;
            tint = new Vector4(rgb.X, rgb.Y, rgb.Z, 1f);
            sectionCenter = new Vector4(section.Center, section.Enabled ? 1f : 0f);
            sectionAlong = new Vector4(section.Along, 0f);
            sectionNormal = new Vector4(section.Normal, 0f);
            sectionHalfSize = new Vector4(section.HalfLength, section.HalfWidth, 0f, 0f);
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
struct PointUniforms
{
    float4x4 view; float4x4 projection; float4 point; float4 tint;
    float4 sectionCenter; float4 sectionAlong; float4 sectionNormal; float4 sectionHalfSize;
};
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
    float radius = sqrt(radiusSquared);
    float feather = max(fwidth(radius), 0.001);
    float coverage = 1.0 - smoothstep(1.0 - feather, 1.0, radius);
    if (coverage <= 0.0) discard_fragment();
    float edge = smoothstep(0.85, 1.0, radius);
    float z = sqrt(max(1.0 - radiusSquared, 0.0));
    float3 normal = float3(p.x, -p.y, z);
    float diffuse = max(dot(normal, normalize(float3(1.0, 1.5, 1.0))), 0.25);
    float3 core = float3(1.0, 0.92, 0.2) * diffuse;
    float3 glow = float3(1.0, 0.7, 0.0);
    return float4(mix(core, glow, edge), in.alpha * coverage);
}";

        /// <summary>
        /// Highlight and preview points, which are handed to the GPU as the CPU-side
        /// <c>PointData</c> with float colors.
        /// </summary>
        private const string PointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct PointVertex { packed_float3 position; packed_float3 color; };
struct PointUniforms
{
    float4x4 view; float4x4 projection; float4 point; float4 tint;
    float4 sectionCenter; float4 sectionAlong; float4 sectionNormal; float4 sectionHalfSize;
};
struct VertexOut { float4 position [[position]]; float point_size [[point_size]]; float3 color; };

vertex VertexOut point_vertex(
    uint vertexId [[vertex_id]],
    const device PointVertex* points [[buffer(0)]],
    constant PointUniforms& uniforms [[buffer(1)]])
{
    PointVertex p = points[vertexId];
    VertexOut out;
    if (uniforms.sectionCenter.w > 0.5)
    {
        float3 delta = float3(p.position) - uniforms.sectionCenter.xyz;
        if (abs(dot(delta, uniforms.sectionAlong.xyz)) > uniforms.sectionHalfSize.x
            || abs(dot(delta, uniforms.sectionNormal.xyz)) > uniforms.sectionHalfSize.y)
        {
            out.position = float4(2.0, 2.0, 2.0, 1.0);
            out.point_size = 0.0;
            out.color = float3(0.0);
            return out;
        }
    }
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
struct PointUniforms
{
    float4x4 view; float4x4 projection; float4 point; float4 tint;
    float4 sectionCenter; float4 sectionAlong; float4 sectionNormal; float4 sectionHalfSize;
};
struct VertexOut { float4 position [[position]]; float point_size [[point_size]]; float3 color; };

vertex VertexOut packed_point_vertex(
    uint vertexId [[vertex_id]],
    const device PackedPointVertex* points [[buffer(0)]],
    constant PointUniforms& uniforms [[buffer(1)]])
{
    PackedPointVertex p = points[vertexId];
    VertexOut out;
    if (uniforms.sectionCenter.w > 0.5)
    {
        float3 delta = float3(p.position) - uniforms.sectionCenter.xyz;
        if (abs(dot(delta, uniforms.sectionAlong.xyz)) > uniforms.sectionHalfSize.x
            || abs(dot(delta, uniforms.sectionNormal.xyz)) > uniforms.sectionHalfSize.y)
        {
            out.position = float4(2.0, 2.0, 2.0, 1.0);
            out.point_size = 0.0;
            out.color = float3(0.0);
            return out;
        }
    }
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
struct AttributePointUniforms
{
    float4x4 view; float4x4 projection; float4 point; float4 tint;
    float4 sectionCenter; float4 sectionAlong; float4 sectionNormal; float4 sectionHalfSize;
};
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
    if (uniforms.sectionCenter.w > 0.5)
    {
        float3 delta = float3(p.position) - uniforms.sectionCenter.xyz;
        if (abs(dot(delta, uniforms.sectionAlong.xyz)) > uniforms.sectionHalfSize.x
            || abs(dot(delta, uniforms.sectionNormal.xyz)) > uniforms.sectionHalfSize.y)
        {
            out.position = float4(2.0, 2.0, 2.0, 1.0);
            out.point_size = 0.0;
            out.color = float3(0.0);
            return out;
        }
    }
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
struct ColorVertexOut
{
    float4 position [[position]];
    // These are measured in framebuffer pixels, so interpolation must happen after the
    // perspective divide just like GLSL's noperspective qualifier.
    float2 lineCoord [[user(locn0), center_no_perspective]];
    float segmentLength [[user(locn1), flat]];
};

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
        out.lineCoord = float2(0.0);
        out.segmentLength = 0.0;
        return out;
    }

    float2 halfViewport = max(uniforms.line.xy, float2(1.0)) * 0.5;
    float2 screenHere  = clipHere.xy  / clipHere.w  * halfViewport;
    float2 screenThere = clipThere.xy / clipThere.w * halfViewport;

    float2 delta = screenThere - screenHere;
    float length2 = dot(delta, delta);
    float2 direction = length2 > 1e-12 ? delta * rsqrt(length2) : float2(1.0, 0.0);
    float2 normal = float2(-direction.y, direction.x);

    float projectedLength = sqrt(length2);
    // Each edge is a round screen-space capsule with a constant pixel diameter, including
    // when the 3D edge is viewed end-on and has an almost zero projected length.
    float outerHalfWidth = uniforms.line.z * 0.5 + 0.5;
    float capExtension = outerHalfWidth;
    float capDirection = atStart ? -1.0 : 1.0;
    float2 offsetNdc = (normal * side * outerHalfWidth
        + direction * capDirection * capExtension) / halfViewport;
    out.position = float4(clipHere.xy + offsetNdc * clipHere.w, clipHere.z, clipHere.w);
    out.lineCoord = float2(atStart ? -outerHalfWidth : projectedLength + outerHalfWidth,
        side * outerHalfWidth);
    out.segmentLength = projectedLength;
    return out;
}

fragment float4 wide_line_fragment(ColorVertexOut in [[stage_in]], constant ColorUniforms& uniforms [[buffer(1)]])
{
    float alongOutside = max(max(-in.lineCoord.x, in.lineCoord.x - in.segmentLength), 0.0);
    float distanceToSegment = length(float2(alongOutside, in.lineCoord.y));
    float halfWidth = uniforms.line.z * 0.5;
    float coverage = 1.0 - smoothstep(halfWidth - 0.5, halfWidth + 0.5, distanceToSegment);
    return float4(uniforms.color.rgb, uniforms.color.a * coverage);
}";

        public static MTLRenderPipelineState CreateWideLinePipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, WideLineShaderSource, "wide_line_vertex", "wide_line_fragment",
                colorFormat, depthFormat, blend: true, sampleCount);

        private const string JoinedLineShaderSource =
@"#include <metal_stdlib>
using namespace metal;
" + JoinedLineShaderCore.MetalHeader + @"
struct ColorUniforms { float4x4 mvp; float4 color; float4 line; };
struct JoinedLineOut
{
    float4 position [[position]];
    // Pixel measurements, so they must interpolate after the perspective divide exactly
    // like GLSL's noperspective qualifier.
    float2 coord [[user(locn0), center_no_perspective]];
    float depth [[user(locn1), center_no_perspective]];
    float2 limits [[user(locn2), flat]];
};
" + JoinedLineShaderCore.Source + @"
// One instance per segment: four points (previous, start, end, next) expanded into a quad.
vertex JoinedLineOut joined_line_segment_vertex(
    uint vertexId [[vertex_id]],
    uint instanceId [[instance_id]],
    const device packed_float3* points [[buffer(0)]],
    constant ColorUniforms& uniforms [[buffer(1)]])
{
    uint base = instanceId * 4u;
    JoinedLineVertex expanded = joinedLineSegment(
        uniforms.mvp * float4(float3(points[base]), 1.0),
        uniforms.mvp * float4(float3(points[base + 1u]), 1.0),
        uniforms.mvp * float4(float3(points[base + 2u]), 1.0),
        uniforms.mvp * float4(float3(points[base + 3u]), 1.0),
        uniforms.line.xy, uniforms.line.z, int(vertexId));
    JoinedLineOut out;
    out.position = expanded.position;
    out.coord = expanded.coord;
    out.depth = expanded.depth;
    out.limits = expanded.limits;
    return out;
}

// One instance per interior joint: three points expanded into the round-join fan.
vertex JoinedLineOut joined_line_join_vertex(
    uint vertexId [[vertex_id]],
    uint instanceId [[instance_id]],
    const device packed_float3* points [[buffer(0)]],
    constant ColorUniforms& uniforms [[buffer(1)]])
{
    uint base = instanceId * 3u;
    JoinedLineVertex expanded = joinedLineJoin(
        uniforms.mvp * float4(float3(points[base]), 1.0),
        uniforms.mvp * float4(float3(points[base + 1u]), 1.0),
        uniforms.mvp * float4(float3(points[base + 2u]), 1.0),
        uniforms.line.xy, uniforms.line.z, int(vertexId));
    JoinedLineOut out;
    out.position = expanded.position;
    out.coord = expanded.coord;
    out.depth = expanded.depth;
    out.limits = expanded.limits;
    return out;
}

fragment float4 joined_line_fragment(
    JoinedLineOut in [[stage_in]], constant ColorUniforms& uniforms [[buffer(1)]])
{
    float coverage = joinedLineCoverage(in.coord, in.limits, in.depth, uniforms.line.z);
    return float4(uniforms.color.rgb, uniforms.color.a * coverage);
}";

        public static MTLRenderPipelineState CreateJoinedLineSegmentPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, JoinedLineShaderSource, "joined_line_segment_vertex", "joined_line_fragment",
                colorFormat, depthFormat, blend: true, sampleCount);

        public static MTLRenderPipelineState CreateJoinedLineJoinPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, int sampleCount = 1)
            => CreatePipeline(device, JoinedLineShaderSource, "joined_line_join_vertex", "joined_line_fragment",
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
