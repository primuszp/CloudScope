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
        /// <summary>All CPU-written buffers fail through the managed error channel instead of dereferencing a nil Metal allocation.</summary>
        public static MTLBuffer AllocateBuffer(MTLDevice device, ulong bytes, MTLResourceOptions storage)
        {
            if (bytes == 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            var buffer = device.NewBuffer(bytes, storage);
            if (buffer.NativePtr == IntPtr.Zero) throw new OutOfMemoryException($"Metal could not allocate {bytes:N0} buffer bytes.");
            if (buffer.Contents == IntPtr.Zero) { Release(buffer.NativePtr); throw new InvalidOperationException("The Metal buffer is not CPU accessible."); }
            return buffer;
        }
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
