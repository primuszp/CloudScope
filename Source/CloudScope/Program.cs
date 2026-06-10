using System;
using System.Diagnostics;
using CloudScope;
using CloudScope.Loading;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: CloudScope <las-file> [max-points]");
    Console.Error.WriteLine("  las-file   Path to a .las or .laz point cloud file");
    Console.Error.WriteLine("  max-points Maximum number of points to load (default: 50 000 000)");
    return 1;
}

string lasFile = args[0];

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

// ── Prepare only the prefix used by fast overview rendering ─────────────────
// This keeps first-K draws spatially representative without shuffling all points.
int progressivePrefix = PointCloudLoader.PrepareProgressiveSubsample(cloud.Points, cloud.LoadedCount);
Console.WriteLine($"Prepared  : {progressivePrefix:N0} points for ProgressiveSubsample prefix");

// ── Compute cloud radius for initial camera fit ──────────────────────────────
Console.WriteLine($"Cloud radius: {cloud.Radius:F1} m");
Console.WriteLine();
Console.WriteLine("Camera controls:");
Console.WriteLine("  Left drag      - Orbit");
Console.WriteLine("  Right drag     - Pan  (clicked point stays under cursor)");
Console.WriteLine("  Scroll         - Zoom (depth-aware)");
Console.WriteLine("  W/A/S/D/Q/E    - FPS navigation");
Console.WriteLine("  Escape         - Exit");
Console.WriteLine();
Console.WriteLine("Command line:");
Console.WriteLine("  Enter / Space  - Submit or repeat the last command");
Console.WriteLine("  Up / Down      - Browse command history");
Console.WriteLine("  Escape         - Cancel the active command");
Console.WriteLine("  Commands       - SELECT, ZOOM, VIEW, PROJECTION, POINTSIZE,");
Console.WriteLine("                   LABEL, SAVELABELS, LOADLABELS, UNDO, HELP");
Console.WriteLine();

// ── Launch viewer ────────────────────────────────────────────────────────────
using var viewer = ViewerHostFactory.Create(1600, 900);
viewer.LoadPointCloud(cloud.Points, cloud.Radius);
viewer.SetLasFilePath(lasFile);
viewer.Run();

return 0;
