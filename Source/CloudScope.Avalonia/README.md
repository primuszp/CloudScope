# CloudScope Avalonia GPU Host

This is a separate spike project for testing an Avalonia desktop shell with an
embedded OpenGL viewport on Windows and a selectable OpenGL/Metal viewport on macOS.

On macOS, Metal is the default. Set `CLOUDSCOPE_RENDER_BACKEND=opengl` to use
the embedded OpenTK host, or `CLOUDSCOPE_RENDER_BACKEND=metal` to explicitly use
the embedded MTKView host.

Run on Windows:

```powershell
dotnet run --project Source\CloudScope.Avalonia\CloudScope.Avalonia.csproj
```

Run on macOS:

```bash
dotnet run --project Source/CloudScope.Avalonia/CloudScope.Avalonia.csproj
```

The app uses `Hosting/HostController.cs` to keep renderer lifecycle, point-cloud
upload, and command handling outside the Avalonia window. Use `Host > Open LAS...`
to load a `.las`/`.laz` file.

Embedded host layout:

| Platform | Host | Native handle |
| --- | --- | --- |
| Shared OpenGL | `Hosting/EmbeddedOpenTkNativeHostBase.cs` | OpenTK lifecycle, command forwarding, key forwarding, frame pump |
| Windows | `Hosting/Platform/Windows/Win32EmbeddedOpenTkNativeHost.cs` | HWND child window |
| macOS OpenGL | `Hosting/Platform/MacOS/MacOsEmbeddedOpenTkNativeHost.cs` | GLFW-backed NSView |
| macOS Metal | `Hosting/Platform/MacOS/MacOsEmbeddedMetalNativeHost.cs` | MTKView-backed NSView |
| Other | `Hosting/AvaloniaOpenGlHostControl.cs` | Placeholder control |

`Hosting/Platform/EmbeddedOpenTkNativeHostFactory.cs` is the only place that
selects the platform-specific embedded host and render backend.

Host lifecycle:

| Step | Shared owner | OpenGL detail | Metal detail |
| --- | --- | --- | --- |
| Create | `ViewportInputHost` asks the factory for a native host | Embeds the GLFW native view | Creates and embeds an `MTKEventView` |
| Initialize | Native host initializes `ViewerController` | Uses `OpenGlRenderBackend` | Uses `MetalRenderBackend` and a Metal command queue |
| Pump | Host runs a 16 ms `DispatcherTimer` | Renders continuously | Drains actions and requests frames when needed |
| Resize | Host receives Avalonia arrange calls | Synchronizes native view and framebuffer size | Synchronizes NSView and drawable pixel size |
| Input | Events are converted to shared viewer input | OpenTK/Avalonia forwarding | Native MTKView and Avalonia key forwarding |
| Destroy | Host stops its timer and disposes the controller | Tears down GL before destroying GLFW | Releases the Metal controller and view delegate |
