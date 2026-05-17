# CloudScope Avalonia OpenGL Host Test

This is a separate spike project for testing an Avalonia desktop shell with an
embedded OpenTK/OpenGL viewport on Windows and macOS.

Run on Windows:

```powershell
dotnet run --project Source\CloudScope.Avalonia\CloudScope.Avalonia.csproj
```

Run on macOS:

```bash
dotnet run --project Source/CloudScope.Avalonia/CloudScope.Avalonia.csproj
```

The test uses `Hosting/HostController.cs` to keep OpenGL lifecycle, point-cloud
upload, and command handling outside the Avalonia window. Use `Host > Open LAS...`
to load a `.las`/`.laz` file. The current spike limits loading to 5 million
points and renders them through the shared OpenTK viewer host.

Embedded host layout:

| Platform | Host | Native handle |
| --- | --- | --- |
| Shared | `Hosting/EmbeddedOpenTkNativeHostBase.cs` | Common viewer lifecycle, command forwarding, key forwarding, frame pump |
| Windows | `Hosting/Platform/Windows/Win32EmbeddedOpenTkNativeHost.cs` | HWND child window |
| macOS | `Hosting/Platform/MacOS/MacOsEmbeddedOpenTkNativeHost.cs` | NSView |
| Other | `Hosting/AvaloniaOpenGlHostControl.cs` | Placeholder control |

`Hosting/Platform/EmbeddedOpenTkNativeHostFactory.cs` is the only place that
selects the platform-specific embedded OpenTK host.

Host lifecycle:

| Step | Shared owner | Windows detail | macOS detail |
| --- | --- | --- | --- |
| Create | `ViewportInputHost` asks the factory for a native host | Creates an OpenTK GLFW window and reparents its HWND into Avalonia | Creates an OpenTK GLFW window and exposes its Cocoa NSView to Avalonia |
| Initialize | `EmbeddedOpenTkViewerHost.InitializeEmbedded()` calls the normal OpenTK load path | Uses the same OpenGL backend as the standalone viewer | Uses the same OpenGL backend as the standalone viewer |
| Pump | Platform host runs a 16 ms `DispatcherTimer` | Pumps queued actions and renders a frame | Pumps queued actions, syncs backing pixels, and renders a frame |
| Resize | Platform host receives Avalonia arrange calls | Calls `SetWindowPos` on the HWND | Updates NSView frame/bounds and synchronizes framebuffer size |
| Input | `Hosting/Input/AvaloniaViewerKeyMapper.cs` converts Avalonia keys to `ViewerKey` | Focus is forwarded to the HWND | Mouse position is read from Cocoa coordinates and converted to viewer coordinates |
| Destroy | Platform host stops the timer and closes the embedded viewer | Releases the HWND reference | Releases the NSView reference |
