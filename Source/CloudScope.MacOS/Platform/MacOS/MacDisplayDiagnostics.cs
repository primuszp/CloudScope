using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CloudScope.Platform.MacOS;

public sealed record MacDisplayStatus(uint MainDisplay, uint ActiveDisplays, int EnumerationStatus, int DisplayLinkStatus)
{
    public bool IsReady => MainDisplay != 0 && EnumerationStatus == 0 && ActiveDisplays > 0 && DisplayLinkStatus == 0;
}

/// <summary>Reads the native prerequisites used by Avalonia's macOS render timer.</summary>
[SupportedOSPlatform("macos")]
public static class MacDisplayDiagnostics
{
    public static MacDisplayStatus Read()
    {
        uint main = CGMainDisplayID();
        int enumeration = CGGetActiveDisplayList(0, IntPtr.Zero, out uint active);
        int status = CVDisplayLinkCreateWithActiveCGDisplays(out IntPtr link);
        if (status == 0 && link != IntPtr.Zero) CVDisplayLinkRelease(link);
        return new(main, active, enumeration, status);
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern uint CGMainDisplayID();
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGGetActiveDisplayList(uint maximum, IntPtr displays, out uint count);
    [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")]
    private static extern int CVDisplayLinkCreateWithActiveCGDisplays(out IntPtr link);
    [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")]
    private static extern void CVDisplayLinkRelease(IntPtr link);
}
