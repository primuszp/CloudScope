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

        public MetalPointUniforms(Matrix4 view, Matrix4 projection, float pointSize)
        {
            this.view = view;
            this.projection = projection;
            point = new Vector4(pointSize, 0f, 0f, 0f);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MetalAttributePointUniforms
    {
        private readonly Matrix4 view;
        private readonly Matrix4 projection;
        private readonly Vector4 point;

        public MetalAttributePointUniforms(Matrix4 view, Matrix4 projection, float pointSize, int colorSource)
        {
            this.view = view;
            this.projection = projection;
            point = new Vector4(pointSize, colorSource, 0f, 0f);
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

        public MetalColorUniforms(Matrix4 mvp, Vector4 color)
        {
            this.mvp = mvp;
            this.color = color;
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
        private const string PointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct PointVertex { packed_float3 position; packed_float3 color; };
struct PointUniforms { float4x4 view; float4x4 projection; float4 point; };
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

        private const string AttributePointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct PointVertex { packed_float3 position; packed_float3 color; };
struct PointAttributes
{
    float zNormalized;
    float intensityNormalized;
    float classCode;
    float returnNumber;
    float red;
    float green;
    float blue;
};
struct PaletteColor { float r; float g; float b; };
struct AttributePointUniforms { float4x4 view; float4x4 projection; float4 point; };
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
    const device PointVertex* points [[buffer(0)]],
    constant AttributePointUniforms& uniforms [[buffer(1)]],
    const device PointAttributes* attributes [[buffer(2)]],
    const device PaletteColor* classPalette [[buffer(3)]])
{
    PointVertex p = points[vertexId];
    PointAttributes a = attributes[vertexId];
    int colorSource = int(uniforms.point.y);

    VertexOut out;
    out.position = uniforms.projection * uniforms.view * float4(p.position, 1.0);
    out.point_size = uniforms.point.x;
    if (colorSource == 0)
        out.color = float3(a.red, a.green, a.blue);
    else if (colorSource == 1)
        out.color = heightColor(a.zNormalized);
    else if (colorSource == 2)
        out.color = paletteColor(classPalette, uint(clamp(a.classCode, 0.0f, 255.0f)));
    else if (colorSource == 3)
        out.color = gradientColor(a.intensityNormalized);
    else if (colorSource == 4)
        out.color = paletteColor(classPalette, uint(clamp(a.returnNumber, 0.0f, 255.0f)));
    else
        out.color = p.color;
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
    const device float3* vertices [[buffer(0)]],
    constant ColorUniforms& uniforms [[buffer(1)]])
{
    ColorVertexOut out;
    out.position = uniforms.mvp * float4(vertices[vertexId], 1.0);
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

        public static MTLRenderPipelineState CreateAttributePointPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, AttributePointShaderSource, "attribute_point_vertex", "attribute_point_fragment",
                colorFormat, depthFormat, blend: false);

        public static MTLRenderPipelineState CreateColorPipeline(
            MTLDevice device, MTLPixelFormat colorFormat, MTLPixelFormat depthFormat)
            => CreatePipeline(device, ColorShaderSource, "color_vertex", "color_fragment",
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
