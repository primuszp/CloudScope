# Rendering backends

CloudScope has OpenGL and Metal render backends behind `IRenderBackend`. The backend owns frame lifecycle, renderer creation, viewport state and depth picking; `ViewerController` owns backend-independent camera, input, selection and labeling workflows.

## Backend selection

- Metal is the default backend on macOS; OpenGL is the default elsewhere and runs through the OpenTK host.
- `CLOUDSCOPE_RENDER_BACKEND=metal` selects the SharpMetal/MTKView host on a supported macOS build.
- `CLOUDSCOPE_RENDER_BACKEND=opengl` keeps the OpenTK/OpenGL host on macOS, including in the Avalonia application.
- Unsupported backend/platform combinations fail during host creation instead of silently falling back.

## Current capability parity

Both backends implement:

- persistent point-cloud GPU buffers and progressive draw budgeting;
- RGB, height, class, intensity and return-number coloring;
- stable source-index mapping and progressive render order;
- label highlights and selection previews;
- box, sphere and cylinder selection gizmos;
- pivot axes, orientation rings and shaded center indicator;
- center crosshair and active-selection-mode indicator;
- depth-buffer picking;
- mouse and keyboard forwarding to `ViewerController`.

CPU-side render data preparation is shared in `CloudScope.Rendering`. Backend implementations should only contain API-specific resource management, shader code and draw commands. In particular, do not duplicate attribute, highlight, overlay-layout or pivot-geometry generation in a backend.

## Point limits

Resident and per-frame draw counts are unlimited by default; the zoom-dependent draw budget still reduces overview density. Optional common limits apply to both backends:

- `CLOUDSCOPE_MAX_RESIDENT_POINTS`
- `CLOUDSCOPE_MAX_DRAW_POINTS`

Backend-specific variables override the common value when present:

- `CLOUDSCOPE_OPENGL_MAX_RESIDENT_POINTS`
- `CLOUDSCOPE_OPENGL_MAX_DRAW_POINTS`
- `CLOUDSCOPE_METAL_MAX_RESIDENT_POINTS`
- `CLOUDSCOPE_METAL_MAX_DRAW_POINTS`

Set `CLOUDSCOPE_FRAME_LOG=1` to print periodic frame-stage timing diagnostics.

## Remaining work

- Validate Metal split-viewport interaction and depth picking with large real-world datasets.
- Move box, sphere and cylinder gizmo mesh generation into shared builders. Their interaction math is shared, but parts of their visual geometry are still generated independently.
- Reuse Metal depth-readback staging buffers and investigate asynchronous picking to avoid allocating and blocking for every read.
- Define identical out-of-bounds behavior for OpenGL and Metal depth-window reads.
- Add automated tests for shared render-data builders and parity-sensitive layout calculations.
- Run macOS smoke tests for Metal shader compilation, split viewports, depth picking and resource teardown. Windows builds validate the C# integration but cannot compile or execute Metal shaders.

## Architecture rules

- Keep GPU resources backend-owned and long-lived.
- Avoid per-frame managed allocations in render paths.
- Keep selection and camera math independent of graphics APIs.
- Put deterministic CPU-side geometry and attribute generation in `CloudScope.Rendering`.
- Keep GLSL/MSL and graphics-API state changes inside their respective backend.
