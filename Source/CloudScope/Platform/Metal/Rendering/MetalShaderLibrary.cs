#if MACOS
using System;
using System.Runtime.InteropServices;
using Foundation;
using Metal;
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

    internal static class MetalBufferWriter
    {
        public static unsafe void Write<T>(IMTLBuffer buffer, T value)
            where T : unmanaged
        {
            *(T*)buffer.Contents = value;
            buffer.DidModifyRange(new NSRange(0, System.Runtime.CompilerServices.Unsafe.SizeOf<T>()));
        }
    }

    internal static class MetalShaderLibrary
    {
        private const string PointShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct PointVertex
{
    float3 position;
    float3 color;
};

struct PointUniforms
{
    float4x4 view;
    float4x4 projection;
    float4 point;
};

struct VertexOut
{
    float4 position [[position]];
    float point_size [[point_size]];
    float3 color;
};

vertex VertexOut point_vertex(
    uint vertexId [[vertex_id]],
    const device PointVertex* points [[buffer(0)]],
    constant PointUniforms& uniforms [[buffer(1)]])
{
    PointVertex point = points[vertexId];
    VertexOut out;
    out.position = uniforms.projection * uniforms.view * float4(point.position, 1.0);
    out.point_size = uniforms.point.x;
    out.color = point.color;
    return out;
}

fragment float4 point_fragment(VertexOut in [[stage_in]], float2 coord [[point_coord]])
{
    float2 d = coord - float2(0.5, 0.5);
    if (dot(d, d) > 0.25)
        discard_fragment();
    return float4(in.color, 1.0);
}";

        private const string ColorShaderSource =
@"#include <metal_stdlib>
using namespace metal;

struct ColorUniforms
{
    float4x4 mvp;
    float4 color;
};

struct ColorVertexOut
{
    float4 position [[position]];
};

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

        public static IMTLRenderPipelineState CreatePointPipeline(
            IMTLDevice device,
            MTLPixelFormat colorPixelFormat,
            MTLPixelFormat depthPixelFormat)
        {
            using var source = new NSString(PointShaderSource);
            using var library = device.CreateLibrary(source, null, out NSError? compileError);
            if (library == null)
                throw new InvalidOperationException("Metal shader compile failed: " + compileError?.LocalizedDescription);

            using var vertex = library.CreateFunction("point_vertex");
            using var fragment = library.CreateFunction("point_fragment");
            using var descriptor = new MTLRenderPipelineDescriptor
            {
                VertexFunction = vertex,
                FragmentFunction = fragment,
                DepthAttachmentPixelFormat = depthPixelFormat
            };
            descriptor.ColorAttachments[0].PixelFormat = colorPixelFormat;

            var pipeline = device.CreateRenderPipelineState(descriptor, out NSError? pipelineError);
            if (pipeline == null)
                throw new InvalidOperationException("Metal point pipeline creation failed: " + pipelineError?.LocalizedDescription);
            return pipeline;
        }

        public static IMTLDepthStencilState? CreateDepthState(IMTLDevice device, bool depthWriteEnabled)
        {
            using var descriptor = new MTLDepthStencilDescriptor
            {
                DepthCompareFunction = MTLCompareFunction.LessEqual,
                DepthWriteEnabled = depthWriteEnabled
            };
            return device.CreateDepthStencilState(descriptor);
        }

        public static IMTLRenderPipelineState CreateColorPipeline(
            IMTLDevice device,
            MTLPixelFormat colorPixelFormat,
            MTLPixelFormat depthPixelFormat)
        {
            using var source = new NSString(ColorShaderSource);
            using var library = device.CreateLibrary(source, null, out NSError? compileError);
            if (library == null)
                throw new InvalidOperationException("Metal color shader compile failed: " + compileError?.LocalizedDescription);

            using var vertex = library.CreateFunction("color_vertex");
            using var fragment = library.CreateFunction("color_fragment");
            using var descriptor = new MTLRenderPipelineDescriptor
            {
                VertexFunction = vertex,
                FragmentFunction = fragment,
                DepthAttachmentPixelFormat = depthPixelFormat
            };
            descriptor.ColorAttachments[0].PixelFormat = colorPixelFormat;
            descriptor.ColorAttachments[0].BlendingEnabled = true;
            descriptor.ColorAttachments[0].SourceRgbBlendFactor = MTLBlendFactor.SourceAlpha;
            descriptor.ColorAttachments[0].DestinationRgbBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;
            descriptor.ColorAttachments[0].SourceAlphaBlendFactor = MTLBlendFactor.One;
            descriptor.ColorAttachments[0].DestinationAlphaBlendFactor = MTLBlendFactor.OneMinusSourceAlpha;

            var pipeline = device.CreateRenderPipelineState(descriptor, out NSError? pipelineError);
            if (pipeline == null)
                throw new InvalidOperationException("Metal color pipeline creation failed: " + pipelineError?.LocalizedDescription);
            return pipeline;
        }
    }
}
#endif
