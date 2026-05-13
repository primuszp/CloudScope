# CloudScope Avalonia OpenGL Host Test

This is a separate spike project for testing an Avalonia desktop shell with an
embedded `OpenGlControlBase` viewport on Windows and macOS.

Run on Windows:

```powershell
dotnet run --project Source\CloudScope.Avalonia.OpenGlHostTest\CloudScope.Avalonia.OpenGlHostTest.csproj
```

Run on macOS:

```bash
dotnet run --project Source/CloudScope.Avalonia.OpenGlHostTest/CloudScope.Avalonia.OpenGlHostTest.csproj
```

The test uses a `HostController` to keep OpenGL lifecycle, point-cloud upload,
and command handling outside the Avalonia control. Use `Host > Open LAS...` to
load a `.las`/`.laz` file. The current spike limits loading to 5 million points,
uploads them to an OpenGL VBO through Avalonia's `GlInterface.GetProcAddress`,
and draws them in a simple orthographic view.
