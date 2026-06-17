using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CloudScope.Platform.MacOS.ObjC;

[SupportedOSPlatform("macos")]
[StructLayout(LayoutKind.Sequential)]
public readonly struct NSPoint
{
    public readonly double X;
    public readonly double Y;

    public NSPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

[SupportedOSPlatform("macos")]
[StructLayout(LayoutKind.Sequential)]
public readonly struct NSSize
{
    public readonly double Width;
    public readonly double Height;

    public NSSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public double X => Width;

    public double Y => Height;
}

[SupportedOSPlatform("macos")]
[StructLayout(LayoutKind.Sequential)]
public readonly struct NSRect
{
    public readonly double X;
    public readonly double Y;
    public readonly double Width;
    public readonly double Height;

    public NSRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public NSPoint Origin => new(X, Y);

    public NSSize Size => new(Width, Height);
}
