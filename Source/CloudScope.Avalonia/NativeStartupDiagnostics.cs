using System.Runtime.InteropServices;
using System.Text.Json;
using CloudScope.Platform.MacOS;
using CloudScope.Rendering;

namespace CloudScope.Avalonia;

internal static class NativeStartupDiagnostics
{
    internal static bool IsRenderTimerFailure(Exception exception) =>
        exception is InvalidOperationException && exception.Message.StartsWith(
            "Avalonia.Native was not able to start the RenderTimer.", StringComparison.Ordinal);

    internal static bool Write(TextWriter output)
    {
        MacDisplayStatus? display = OperatingSystem.IsMacOS() ? MacDisplayDiagnostics.Read() : null;
        output.WriteLine(JsonSerializer.Serialize(new
        {
            operatingSystem = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            renderer = RenderBackendFactory.GetDefaultKind().ToString(),
            display,
            note = "Display readiness is a snapshot; it does not validate GPU rendering or picking."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return display?.IsReady ?? true;
    }
}
