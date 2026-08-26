using Avalonia.Controls;
using CloudScope.Avalonia.Hosting.Platform.MacOS;
using CloudScope.Avalonia.Hosting.Platform.Windows;
using CloudScope.Rendering;

namespace CloudScope.Avalonia.Hosting.Platform;

internal static class EmbeddedViewerHostFactory
{
    public static Control Create(HostController hostController)
    {
        if (OperatingSystem.IsWindows())
            return new Win32EmbeddedOpenGlHost(hostController);

        if (OperatingSystem.IsMacOS())
            return RenderBackendFactory.GetDefaultKind() == RenderBackendKind.Metal
                ? new MacOsEmbeddedMetalHost(hostController)
                : new MacOsEmbeddedOpenGlHost(hostController);

        return new AvaloniaOpenGlHostControl();
    }
}
