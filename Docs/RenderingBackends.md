# Rendering Backend Roadmap

CloudScope currently runs through the OpenGL backend.

The backend boundary is intentionally small:

- `IRenderBackend` owns frame state, clear, resize, and renderer creation.
- `IPointCloudRenderer` owns point-cloud GPU upload and draw.
- `IOverlayRenderer` owns pivot/crosshair/mode overlay rendering.
- `RenderBackendFactory` selects the backend. `CLOUDSCOPE_RENDER_BACKEND=metal` selects the MTKView-backed host on `net10.0-macos`; other builds fail fast with a platform message.
- `OpenTkViewerHost` is the current OpenTK/GameWindow host. `PointCloudViewer` is only a compatibility facade.
- `MtkViewerHost` owns the macOS `MTKView` lifecycle and forwards draw/resize callbacks into `ViewerController`.

Known OpenGL dependencies that must move before a real Metal build:

- `MtkViewerHost` currently owns the Metal view lifecycle, but macOS mouse/keyboard event forwarding still needs to be wired to `ViewerController`.
- `MetalRenderBackend` currently creates the Metal frame clear/present path. Point cloud, label/preview highlight, overlay primitives, and first-pass selection gizmos have Metal renderers; depth picking is still a no-op placeholder.
- Gizmo renderers still call OpenGL directly: `GizmoRendererBase`, `BoxGizmoRenderer`, `SphereGizmoRenderer`, `CylinderGizmoRenderer`.
- `HighlightRenderer` still calls OpenGL directly.
- `OrbitCamera` uses `IDepthPicker`; OpenGL currently provides `OpenGlDepthPicker`. Metal needs its own depth texture/readback or GPU picking path.
- Shaders are GLSL strings. Metal needs equivalent MSL shaders and a shared shader/material description above both backends.

Target split:

- `CloudScope.Core`: point data, loading, camera math, selection queries, labels.
- `CloudScope.Platform.OpenGL`: OpenTK window host, OpenGL backend glue.
- `CloudScope.Platform.Metal`: future MTKView host, Metal backend glue.
- `CloudScope.App`: startup, command-line parsing, backend/platform selection.

Host split:

- `ViewerController`: camera/input/selection/label workflow and backend-agnostic render orchestration.
- `OpenTkViewerHost`: adapts OpenTK events to `ViewerController`.
- `MtkViewerHost`: future adapter for MTKView draw/input/resize callbacks.

Performance rule:

- Keep point buffers backend-owned and long-lived.
- Avoid per-frame allocations in render paths.
- Keep selection resolution independent from the render backend unless GPU picking is explicitly introduced.
