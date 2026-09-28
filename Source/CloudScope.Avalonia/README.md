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
upload, and command handling outside the Avalonia window. Use `File > Open LAS/LAZ...`,
the ribbon's **Open** button, or type `OPEN` to load a `.las`/`.laz` file.

Workspace layout (see `Docs/UiRedesign.md` §11):

| Region | Control | Command |
| --- | --- | --- |
| Ribbon (Home, View, Display, Label, Analyze, Output) | `Controls/RibbonControl.cs` | `RIBBON [On/Off/Toggle]` |
| Explorer — clouds, layers, label classes, scene objects, views | `Controls/ExplorerPanel.cs` | `EXPLORER [On/Off/Toggle/Left/Right]` |
| Properties of the explorer selection | `Controls/PropertiesPanel.cs` | `PROPERTIES [On/Off/Toggle]` |
| Command line and history | `Controls/CommandLineControl.cs`, `CommandHistoryWindow.cs` | `COMMANDLINE`, `HISTORY` (F2) |
| Status bar toggles | `MainWindow.cs` | `ORTHO`, `PROJECTION`, `LABELMODE`/`NAVIGATE`, … |

Every button, tree row, property editor and menu item submits a command string, so
everything in the UI is also scriptable and appears in the command history.

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
