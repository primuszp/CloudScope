# Integrated point-cloud exchange tools

CloudScope implements the useful Open Pointcloud Studio workflows in its existing .NET architecture. Commands, menus and the local API use the same command registry. These are original C# implementations; the reference project is LGPL and its TypeScript was not copied into the MIT codebase.

## Commands

| Command | Function |
| --- | --- |
| OPENPLY / OPENXYZ / OPENPTS | Resident text/binary point import |
| OPENPTX | PTX grids, multiple scans, scanner transforms, intensity and RGB |
| OPENE57 | E57 numeric bitpack streams, multiple scans, Cartesian/spherical coordinates, poses, invalid-point masks and CRC32C checks |
| THIN 25 / THIN 100 | Repeatable thinning / restore full filtered density |
| EXPORT PlyBinary path.ply | Binary PLY export |
| EXPORT PlyAscii path.ply | ASCII PLY export |
| EXPORT Xyz / Pts / Csv path | Other point exports |
| RECONSTRUCT 15 0 | Estimate local normals and triangulate; 15 neighbours, automatic maximum edge |
| SURFACE On / Off / Clear | Show, hide or discard the reconstructed surface |
| EXPORTOBJ path.obj | Export the surface in original coordinates |
| API On 47830 / API Off | Enable / stop authenticated loopback HTTP automation |

File-open prompts accept paths containing spaces. In a complete command quote the path, for example `OPENXYZ "scan one.xyz" 5000`; at a separate file prompt type the full path, then answer the point-limit prompt (Enter means all). Import remains resident in memory; these commands do not create a streaming tile index.

Exports contain the current attribute-filtered and thinned sample, original RGB, mapped attributes and current classification annotations. Coordinates are restored to the original frame. Viewport section clipping is not an export filter. PLY preserves intensity/classification; XYZ preserves coordinates/RGB; PTS adds normalized intensity; CSV names each available column. Export uses a temporary file and protects the loaded source path.

## Cloud panel

The Avalonia inspector shows the resident source, streaming store layers and point counts. Store checkboxes control visibility, the close buttons remove layers, and Add tile store runs the existing ADDSTORE command. Surface counts and reconstruction progress appear in the same inspector.

## Surface limits

Reconstruction operates on the current resident sample, without a fixed point-count ceiling. Available memory and execution time still constrain large inputs. Thin large inputs when needed. It uses exact k-d-tree nearest-neighbour queries, scale-normalized analytic PCA, centroid-based normal orientation, and angular fan triangulation. Unlike the reference grid search, neighbour discovery has no five-cell cutoff and cannot stop merely because a cell cube contains enough points. Automatic maximum edge length retains twice the reference density-derived cell size; this is an approximate visualization surface, not a watertight solid or a replacement for a CAD mesher. Filtering, thinning, replacing or resetting the cloud invalidates its surface and cancels the current reconstruction. Surface Clear resets progress immediately and permits a new reconstruction without waiting for an obsolete completion callback. Worker and upload failures leave the operation available for retry. OBJ uses one-based face indices, world coordinates, per-vertex normals and optional vertex RGB. Both OpenGL and Metal display the surface. Viewport section clipping is currently applied to points only; the reconstructed mesh remains whole.

## E57 limits

The reader supports standard numeric bitpack streams with Float, Integer and ScaledInteger prototypes. Explicit alternative codecs and unsupported prototype types fail with an error. Image2D records are not imported. Checksums are validated before decoding. Loading can consume substantial memory because scan streams and resident points are held in memory.

## Local API

Disabled by default. API On binds only 127.0.0.1. All requests require the per-process bearer token from the discovery JSON under the platform LocalApplicationData directory, CloudScope/instances/<pid>.json. On Unix it is owner-readable only. The file is removed when the server stops.

- GET /health: server health
- GET /status: viewer status
- POST /exec with JSON {"command":"THIN 25"}: dispatch through the existing command runtime

Use Authorization: Bearer <token>. Commands execute on the viewer thread and return the normal command result, including any pending prompt. File-open commands accept a quoted path and point limit in one request, or separate prompt-answer requests. HTTP replies acknowledge command dispatch; imports/reconstruction finish asynchronously, so inspect status for completion. There is no code-evaluation endpoint.

## Verification

Run `dotnet build Source/CloudScope.slnx -warnaserror` and `dotnet run --project Source/CloudScope.CommandChecks --no-build`.

Exchange checks cover all five point exports, PTX transforms/truncation, an independently generated libE57 multi-scan fixture, E57 checksum rejection, triangulation/OBJ indices, and authenticated HTTP command dispatch. Four reconstruction fixtures generated by the original TypeScript implementation check triangle indices/winding and PCA normals. A cancellation test verifies that an input above 200,000 points reaches indexing without a point-count rejection. Exact neighbours are additionally checked against brute force on uniform/nonuniform samples and duplicates, and planar normals are checked across coordinate scales from 1e-6 to 1e6. The current review verifies command workflows without a GPU and renders the Avalonia shell with its native viewport replaced. Native Metal/OpenGL rendering and picking still need a desktop run; see [CodeReview.md](CodeReview.md).

### Reconstruction adaptations

The C# implementation expresses the reference numerical algorithm independently, without including the LGPL TypeScript source. It retains the angular fan topology and does not impose a two-faces-per-edge restriction. The exact spatial index and scale-normalized PCA are deliberate robustness corrections to the reference neighbourhood search and absolute eigenvector thresholds. Like the reference, the fan method can produce overlapping or nonmanifold faces; it is not a Delaunay triangulation or watertight mesher. We retain CloudScope's Z-up fallback, reject exactly zero-area triangles, and add cancellation checks inside long-running phases. The neighbor command accepts any integer >= 3. The old 200,000-point guard has been removed from both the algorithm and viewer command.
