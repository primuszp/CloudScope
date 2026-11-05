using Avalonia;

namespace CloudScope.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--diagnostics"])
            return NativeStartupDiagnostics.Write(Console.Out) ? 0 : 2;
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("Usage: CloudScope.Avalonia [--diagnostics]");
            return 0;
        }
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (InvalidOperationException exception) when (NativeStartupDiagnostics.IsRenderTimerFailure(exception))
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("The native display timer is unavailable. Unlock/wake the graphical session, then retry. Use --diagnostics to inspect display readiness.");
            return 2;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
