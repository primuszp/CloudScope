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

File-open prompts accept paths containing spaces. Submit the file path first, then answer the maximum-point-count prompt separately (Enter means all). Import remains resident in memory; these commands do not create a streaming tile index.

Exports contain the current attribute-filtered and thinned sample, original RGB, mapped attributes and current classification annotations. Coordinates are restored to the original frame. Viewport section clipping is not an export filter. PLY preserves intensity/classification; XYZ preserves coordinates/RGB; PTS adds normalized intensity; CSV names each available column. Export uses a temporary file and protects the loaded source path.

## Cloud panel

The Avalonia inspector shows the resident source, streaming store layers and point counts. Store checkboxes control visibility, the close buttons remove layers, and Add tile store runs the existing ADDSTORE command. Surface counts and reconstruction progress appear in the same inspector.

## Surface limits

Reconstruction operates on the current resident sample, with a 200,000-point limit. Thin large inputs first. It estimates normals with local PCA and forms a greedy local triangulation; this is an approximate visualization surface, not a watertight solid or a replacement for a CAD mesher. Filtering, thinning, replacing or resetting the cloud invalidates its surface. OBJ uses one-based face indices and world coordinates. Both OpenGL and Metal display the surface. Viewport section clipping is currently applied to points only; the reconstructed mesh remains whole.

## E57 limits

The reader supports standard numeric bitpack streams with Float, Integer and ScaledInteger prototypes. Explicit alternative codecs and unsupported prototype types fail with an error. Image2D records are not imported. Checksums are validated before decoding. Loading can consume substantial memory because scan streams and resident points are held in memory.

## Local API

Disabled by default. API On binds only 127.0.0.1. All requests require the per-process bearer token from the discovery JSON under the platform LocalApplicationData directory, CloudScope/instances/<pid>.json. On Unix it is owner-readable only. The file is removed when the server stops.

- GET /health: server health
- GET /status: viewer status
- POST /exec with JSON {"command":"THIN 25"}: dispatch through the existing command runtime

Use Authorization: Bearer <token>. Commands execute on the viewer thread and return the normal command result, including any pending prompt. File-open commands require a second request to answer the point-limit prompt. HTTP replies acknowledge command dispatch; imports/reconstruction finish asynchronously, so inspect status for completion. There is no code-evaluation endpoint.

## Verification

Run `dotnet build Source/CloudScope.slnx -warnaserror` and `dotnet run --project Source/CloudScope.CommandChecks --no-build`.

Exchange checks cover all five point exports, PTX transforms/truncation, an independently generated libE57 multi-scan fixture, E57 checksum rejection, triangulation/OBJ indices, and authenticated HTTP command dispatch. The macOS Metal viewer was also exercised with a 400-point sloped plane, reconstruction, live API exports and thinning. OpenGL surface rendering has build-level verification only.
