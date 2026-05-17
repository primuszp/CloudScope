using Avalonia.Controls;
using CloudScope.Avalonia.Hosting.Platform.MacOS;
using CloudScope.Avalonia.Hosting.Platform.Windows;

namespace CloudScope.Avalonia.Hosting.Platform;

internal static class EmbeddedOpenTkNativeHostFactory
{
    public static Control Create(HostController hostController)
    {
        if (OperatingSystem.IsWindows())
            return new Win32EmbeddedOpenTkNativeHost(hostController);

        if (OperatingSystem.IsMacOS())
            return new MacOsEmbeddedOpenTkNativeHost(hostController);

        return new AvaloniaOpenGlHostControl();
    }
}
