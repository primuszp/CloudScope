using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CloudScope.Platform.Metal.ObjC
{
    [SupportedOSPlatform("macos")]
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NSSize
    {
        public readonly double Width;
        public readonly double Height;

        public NSSize(double width, double height)
        {
            Width  = width;
            Height = height;
        }
    }
}
