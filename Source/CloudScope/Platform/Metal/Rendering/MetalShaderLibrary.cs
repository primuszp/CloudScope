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
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, PointShaderSource, "point_vertex", "point_fragment",
                colorFormat, depthFormat, blend: false);

        public static MTLRenderPipelineState CreatePackedPointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, PackedPointShaderSource, "packed_point_vertex", "packed_point_fragment",
                colorFormat, depthFormat, blend: false);

        public static MTLRenderPipelineState CreateAttributePointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, AttributePointShaderSource, "attribute_point_vertex", "attribute_point_fragment",
                colorFormat, depthFormat, blend: false);

        private const string WideLineShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct ColorUniforms { float4x4 mvp; float4 color; float4 line; };
struct ColorVertexOut { float4 position [[position]]; };

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
        return out;
    }

    float2 halfViewport = max(uniforms.line.xy, float2(1.0)) * 0.5;
    float2 screenHere  = clipHere.xy  / clipHere.w  * halfViewport;
    float2 screenThere = clipThere.xy / clipThere.w * halfViewport;

    float2 delta = screenThere - screenHere;
    float length2 = dot(delta, delta);
    float2 direction = length2 > 1e-12 ? delta * rsqrt(length2) : float2(1.0, 0.0);
    float2 normal = float2(-direction.y, direction.x);

    float2 offsetNdc = normal * side * (uniforms.line.z * 0.5) / halfViewport;
    out.position = float4(clipHere.xy + offsetNdc * clipHere.w, clipHere.z, clipHere.w);
    return out;
}

fragment float4 wide_line_fragment(ColorVertexOut in [[stage_in]], constant ColorUniforms& uniforms [[buffer(1)]])
{
    return uniforms.color;
}";

        public static MTLRenderPipelineState CreateWideLinePipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, WideLineShaderSource, "wide_line_vertex", "wide_line_fragment",
                colorFormat, depthFormat, blend: true);

        public static MTLRenderPipelineState CreateColorPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, ColorShaderSource, "color_vertex", "color_fragment",
                colorFormat, depthFormat, blend: true);

        public static MTLRenderPipelineState CreatePivotPointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, PivotPointShaderSource, "pivot_point_vertex", "pivot_point_fragment",
                colorFormat, depthFormat, blend: true);

        public static MTLDepthStencilState CreateDepthState(MTLDevice device, bool depthWrite)
        {
            var desc = new MTLDepthStencilDescriptor();
            desc.DepthCompareFunction = MTLCompareFunction.LessEqual;
            desc.IsDepthWriteEnabled = depthWrite;
            return device.NewDepthStencilState(desc);
        }

        private static MTLRenderPipelineState CreatePipeline(
            MTLDevice device, string source, string vertFn, string fragFn,
            MTLPixelFormat colorFormat, MTLPixelFormat depthFormat, bool blend)
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
