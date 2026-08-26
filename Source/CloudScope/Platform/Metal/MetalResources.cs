using System;
using System.Runtime.Versioning;
using CloudScope.Platform.MacOS.ObjC;
using SharpMetal.Metal;

namespace CloudScope.Platform.Metal
{
    /// <summary>
    /// Lifetime helpers for the Metal objects the renderers create. SharpMetal hands out
    /// bare handles, so every renderer released them by hand; they all release them here now.
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal static class MetalResources
    {
        /// <summary>Releases a Metal object handle, ignoring null.</summary>
        public static void Release(IntPtr nativePtr) => ObjectiveCRuntime.Release(nativePtr);

        /// <summary>Releases a buffer and resets the caller's field to the default handle.</summary>
        public static void Release(ref MTLBuffer buffer)
        {
            ObjectiveCRuntime.Release(buffer.NativePtr);
            buffer = default;
        }
    }
}
