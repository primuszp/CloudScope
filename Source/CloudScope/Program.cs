using System;
using System.Diagnostics;
using CloudScope;
using CloudScope.Loading;

string lasFile = args.Length > 0
    ? args[0]
    : @"/Users/primuszpeter/Library/CloudStorage/OneDrive-Személyes/BorderEye/data/jeli_parkolo.las";

//string lasFile = args.Length > 0
//    ? args[0]
//    : @"D:\Personal\OneDrive\BorderEye\data\jeli_parkolo.las";

if (!System.IO.File.Exists(lasFile))
{
    Console.Error.WriteLine($"Error: File not found: {lasFile}");
    Console.Error.WriteLine("Usage: CloudScope <las-file> [max-points]");
    return 1;
}

long maxPoints = args.Length > 1 && long.TryParse(args[1], out long m) ? m : 50_000_000;

// ── Open and report header ───────────────────────────────────────────────────
Console.WriteLine($"LAS file: {lasFile}");
using var reader = new CloudScope.Library.LasReader(lasFile);
var hdr = reader.Header;

bool hasColor = hdr.PointDataFormatId is 2 or 3 or 5 or 7 or 8 or 10;
Console.WriteLine($"LAS version : {hdr.VersionMajor}.{hdr.VersionMinor}");
Console.WriteLine($"Point format: {hdr.PointDataFormatId}  (colored: {hasColor})");
Console.WriteLine($"Point stride: {hdr.PointDataRecordLength} bytes");
Console.WriteLine($"Total points: {reader.PointCount:N0}");
Console.WriteLine($"Bounds X    : [{hdr.MinX:F2} ... {hdr.MaxX:F2}]");
Console.WriteLine($"Bounds Y    : [{hdr.MinY:F2} ... {hdr.MaxY:F2}]");
Console.WriteLine($"Bounds Z    : [{hdr.MinZ:F2} ... {hdr.MaxZ:F2}]");

if (maxPoints < reader.PointCount)
    Console.WriteLine($"Load limit  : {maxPoints:N0} points (out of {reader.PointCount:N0})");

// ── Load and convert points in a single streaming pass ──────────────────────
Console.Write("Loading");
var sw = Stopwatch.StartNew();

var progress = new Progress<int>(pct => Console.Write($"\rLoading {pct,3}%"));
var cloud = PointCloudLoader.Load(reader, maxPoints, progress);

sw.Stop();
Console.WriteLine($"\rLoaded : {cloud.LoadedCount:N0} points  ({sw.Elapsed.TotalSeconds:F1} s)");
if (cloud.HasColor && Math.Abs(cloud.ColorScale - (1.0f / 255.0f)) < 0.000001f)
    Console.WriteLine("Info: Detected 8-bit colors (0-255). Scaling corrected.");

// ── Fisher-Yates shuffle so first-K points give uniform spatial coverage ────
// ProgressiveSubsample draws only the first N points based on zoom level.
PointCloudLoader.ShuffleForProgressiveSubsample(cloud.Points, cloud.LoadedCount);
Console.WriteLine("Shuffled  : points reordered for ProgressiveSubsample");

// ── Compute cloud radius for initial camera fit ──────────────────────────────
Console.WriteLine($"Cloud radius: {cloud.Radius:F1} m");
Console.WriteLine();
Console.WriteLine("Camera controls:");
Console.WriteLine("  Left drag      - Orbit");
Console.WriteLine("  Right drag     - Pan  (clicked point stays under cursor)");
Console.WriteLine("  Scroll         - Zoom (depth-aware)");
Console.WriteLine("  Space          - Toggle Perspective / Orthographic");
Console.WriteLine("  W/A/S/D/Q/E    - FPS navigation");
Console.WriteLine("  Num1/3/7       - Front / Right / Top view");
Console.WriteLine("  Num5           - Isometric preset");
Console.WriteLine("  Home           - Reset view");
Console.WriteLine("  +  /  -        - Point size");
Console.WriteLine("  Escape         - Exit");
Console.WriteLine();
Console.WriteLine("Label mode  (L to toggle):");
Console.WriteLine("  Left drag      - Draw selection rectangle  →  enters Edit mode");
Console.WriteLine("  Drag face arrow- Resize box on that axis");
Console.WriteLine("  Drag corner    - Resize box diagonally");
Console.WriteLine("  Drag center    - Move box");
Console.WriteLine("  Drag ring      - Rotate box (local X / Y / Z)");
Console.WriteLine("  Camera         - Orbit / pan / zoom always active");
Console.WriteLine("  Enter          - Confirm: label enclosed points");
Console.WriteLine("  Escape         - Cancel box / exit label mode");
Console.WriteLine("  Del            - Clear last labeled selection");
Console.WriteLine("  1 / 2          - Switch tool: Box / Sphere");
Console.WriteLine("  3 – 9          - Label: Ground / Building / Vegetation /");
Console.WriteLine("                   Vehicle / Road / Water / Wire");
Console.WriteLine("  G / S / R      - Grab / Scale / Rotate (keyboard + mouse drag)");
Console.WriteLine("  X / Y / Z      - Constrain keyboard edit to axis");
Console.WriteLine("  Ctrl+Z         - Undo last label");
Console.WriteLine("  Ctrl+S         - Save labels (.labels.json)");
Console.WriteLine("  Ctrl+O         - Load labels");
Console.WriteLine();

// ── Launch viewer ────────────────────────────────────────────────────────────
using var viewer = ViewerHostFactory.Create(1600, 900);
viewer.LoadPointCloud(cloud.Points, cloud.Radius);
viewer.SetLasFilePath(lasFile);
viewer.Run();

return 0;

