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
