# CloudScope

A .NET 10 point-cloud viewer with a shared command system, OpenGL and macOS Metal rendering, and an Avalonia desktop workspace.

## Build and run

The SDK selection is recorded in `global.json`; package versions and shared build settings live in `Source/Directory.Packages.props` and `Source/Directory.Build.props`.

```bash
dotnet build Source/CloudScope.slnx -warnaserror
dotnet run --project Source/CloudScope.Avalonia
```

On macOS, build a locally signed application bundle for the current architecture:

```bash
scripts/package-macos.sh
open artifacts/macos/CloudScope.app
artifacts/macos/CloudScope.app/Contents/MacOS/CloudScope.Avalonia --diagnostics
```

The bundle requires the installed .NET 10 runtime; it is a local development package, not a notarized distribution. `--diagnostics` runs without opening a window and reports active display/DisplayLink readiness (exit 0 when ready, 2 otherwise). If native startup reports RenderTimer error `-6661`, unlock/wake the graphical session and retry. A locked session can expose no active displays even when a main-display id exists.

The Avalonia embedded viewport supports Windows and macOS. The standalone OpenTK viewer is also available:

```bash
dotnet run --project Source/CloudScope -- --help
dotnet run --project Source/CloudScope -- "/path/to/scan.las" 5000000
```

The optional point limit is zero for all points; resident clouds must fit memory and the .NET array limit. Metal is the default on macOS. Set `CLOUDSCOPE_RENDER_BACKEND=opengl` to select OpenGL.

## Workflows

- Save and reopen the complete workspace with `SAVEPROJECT` (`SAVE`), `SAVEAS` and `OPENPROJECT`. Projects retain labels, drawings, cross-sections, cameras, layers, display settings and reconstructed surfaces; source surveys remain relative external references. `RECOVER` restores an abandoned automatic recovery snapshot. Cmd/Ctrl+S saves the project. See [project persistence](Docs/Projects.md).
- Import uncompressed LAS 1.0–1.4 (point formats 0–10), PLY, XYZ, PTS, PTX and supported E57 numeric scans. **Compressed LAZ is not decoded**; decompress it to LAS first.
- Index LAS into streaming tile stores with `INDEX`, then use `OPENSTORE`, `ADDSTORE` and `LAYER` for large surveys.
- Filter attributes, choose RGB/height/class/intensity/return colouring, and thin a repeatable sample with `THIN` while preserving source indices.
- Create selection volumes, annotations and tree instances; run CSF ground filtering and seed-assisted terrestrial/SLAM tree segmentation. Save/load JSON labels for every resident import format; LAS classification export requires a LAS source. New label files include an ordered-geometry checksum, point count and coordinate frame. Ordinary loading verifies that identity; use `LOADLABELS Transfer "old-labels.json"` only for an intentional index-based transfer or legacy migration.
- Draw 3D and planar polylines, work with cross-sections and split viewports, reconstruct a visualization surface, and export points or OBJ meshes.
- Drive the same operations through the command line, menus, ribbon, scripts and an optional authenticated local HTTP API.

For arguments containing spaces, use quotes: `OPENXYZ "scan one.xyz" 5000`, `LABEL "Tree 1" 7`. A command with missing arguments asks for them. File imports, reconstruction, indexing and forestry segmentation finish in the background; wait for completion before running dependent operations. `CANCEL` or Escape cancels background work. Reset, source replacement, surface clearing and closing cancel the corresponding work and discard obsolete results. Segmentation publishes one undo operation on the viewer thread and is discarded if labels change before publication. Indexing stages its output and publishes only a complete store; choose a new or empty output directory.

Label saves and planar-polyline saves replace complete files atomically. Save checkpoints follow undo/redo, including deletion of every label; failed saves keep the document open. The viewer's undo history retains up to 1000 commands within an estimated 128 MiB budget and reports when old entries are released. The Metal draw-buffer cache retains up to 64 MiB after GPU completion; shared polyline tessellation uses a 32 MiB cache. These are cache/history budgets, not a cap on the full survey's memory use. `TIME` reports their diagnostics.

[Command inventory](Docs/CommandInventory.md) · [Command system](Docs/CommandSystem.md) · [Exchange formats and limitations](Docs/OpenPointcloudIntegration.md) · [Rendering backends](Docs/RenderingBackends.md) · [Design system](Docs/DesignSystem.md)

## Controls

Left drag orbits; right drag pans; the wheel zooms around the picked depth. W/A/S/D/Q/E moves the camera. Standard views, projection, point size and layout are available through commands and the ribbon. Escape cancels the active operation; `QUIT` closes the viewer.

For individual-tree segmentation, load a resident cloud, run `GROUNDSEG`, then `TREESEG` and pick a trunk seed. Ground points are excluded from tree growth. The result is a `Tree` annotation with an instance id and participates in undo and label saving. Segmentation is a heuristic for terrestrial/SLAM scans, not a validated universal forestry classifier.

## Verification

```bash
dotnet run --project Source/CloudScope.CommandChecks
dotnet run --project Source/CloudScope.UiChecks
dotnet run --project Source/CloudScope.CommandChecks -c Release -- --benchmark
# On a Mac with a real Metal device (no visible window required):
dotnet run --project Source/CloudScope.CommandChecks -c Release -- --metal-gpu
```

CommandChecks covers registration, all command/alias startup paths on a headless viewer, command conversations, data exchange, source identity, undo, reconstruction and numerical edge cases. UiChecks renders the actual Avalonia shell and all ribbon tabs at two window sizes, saving PNGs in a temporary directory (or a directory supplied as its first argument). It replaces the native 3D viewport, so it does not validate GPU rendering or native picking.

The ordinary GitHub Actions workflow builds Debug/Release and runs the command/UI checks on Linux, Windows and macOS, checks the generated command inventory, records the allocation benchmark and builds the portable viewer. The separate manual Metal workflow requires a self-hosted Apple Silicon Mac with the `cloudscope-gpu` runner label. Adding the workflows does not register a runner or execute a remote run.

See [the code review and verification record](Docs/CodeReview.md) for the scope and remaining native checks.

## Project structure

- `CloudScope.Core`: formats, point data, spatial and forestry algorithms, selection geometry, labels, drawing and the command runtime.
- `CloudScope`: viewer/controller, command implementations, shared rendering preparation, OpenGL and Metal backends.
- `CloudScope.MacOS`: AppKit/Metal interop.
- `CloudScope.Avalonia`: desktop shell and native viewport adapters.
- `CloudScope.CommandChecks`, `CloudScope.UiChecks`: reproducible checks.
- `MetalTriangleTest`, `MetalNetTriangle`: standalone graphics experiments.

Licensed under the MIT License.
