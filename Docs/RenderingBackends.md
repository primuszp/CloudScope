# Rendering Backend Roadmap

CloudScope currently runs through the OpenGL backend.

The backend boundary is intentionally small:

- `IRenderBackend` owns frame state, clear, resize, and renderer creation.
- `IPointCloudRenderer` owns point-cloud GPU upload and draw.
- `IOverlayRenderer` owns pivot/crosshair/mode overlay rendering.
- `RenderBackendFactory` selects the backend. `CLOUDSCOPE_RENDER_BACKEND=metal` is reserved and fails fast until implemented.
- `OpenTkViewerHost` is the current OpenTK/GameWindow host. `PointCloudViewer` is only a compatibility facade.

Known OpenGL dependencies that must move before a real Metal build:

- `OpenTkViewerHost` still owns input, lifecycle, and render orchestration. Metal should use a separate MTKView-backed host instead of OpenTK `GameWindow`.
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
