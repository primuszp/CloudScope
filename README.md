# CloudScope

A high-performance LAS (LiDAR) point cloud viewer built with C# and .NET 10.0.

## Features

- **Fast Point Cloud Rendering**: Efficiently visualizes millions of LAS points using OpenGL
- **Color Support**: Automatically detects colored and non-colored point formats
- **Smart Color Scaling**: Handles both 8-bit (0-255) and 16-bit (0-65535) color values
- **Intuitive Camera Controls**:
  - Left Mouse: Orbit
  - Right Mouse: Pan
  - Scroll Wheel: Zoom (depth-aware)
  - Space: Toggle Perspective / Orthographic
  - W/A/S/D/Q/E: FPS Navigation
  - Num1/3/7/5: Standard views (Front/Right/Top/Isometric)
- **Point Limit**: Load partial datasets for faster preview (default: 30M points)
- **Individual-tree segmentation**: Pick a trunk seed in a terrestrial/SLAM cloud; multi-source
  3D graph growth separates the selected tree from automatically detected neighbouring trunks

## Requirements

- .NET 10.0 SDK
- Windows, macOS, or Linux with OpenGL support

## Build

```bash
cd Source
dotnet build
```

## Usage

```bash
cd Source/CloudScope/bin/Debug/net10.0
CloudScope.exe <path-to-file.las> [max-points]
```

**Examples:**
```bash
CloudScope.exe data.las                    # Load entire file
CloudScope.exe data.las 5000000           # Load up to 5M points
```

### Segmenting an individual tree

Load a resident LAS/LAZ cloud and run `GROUNDSEG` first to classify terrain points with the
progressive multi-scale ground filter. Then enter `TREESEG` (aliases: `TREE`, `SEGMENTTREE`) and click a
point on the target trunk. The result is highlighted as a `Tree` annotation with an automatically
assigned instance id, so it participates in the existing undo and label-save workflow. The current
implementation is tuned for terrestrial/SLAM scans; streamed point-tile stores must first be loaded
as a resident cloud. Ground points receive the standard LAS class 2 when labels are exported, and
are excluded from tree graph growth.

## Project Structure

- **CloudScope**: Main viewer application with UI and rendering
- **CloudScope.Library**: Core LAS file reading and data structures
  - `LasReader`: LAS file parsing
  - `LasHeader`: LAS header information
  - `LasPoint`: Individual point data
  - `HeaderBlock`: Structured header data
  - `ClassificationType`: Point classification enums

## LAS Support

Supports LAS format versions 1.0-1.4 with point formats:
- Format 0-3: Basic point formats
- Format 5-10: Extended formats with return info

Automatically handles:
- Different point record lengths
- 8-bit and 16-bit color spaces
- Height-based coloring for non-colored point clouds

## Dependencies

- [OpenTK 4.9.4](https://opentk.net/): OpenGL bindings for C#
- .NET Runtime with OpenGL support

## License

Licensed under the MIT License.

## Author

Created as a personal project for LiDAR point cloud visualization.
