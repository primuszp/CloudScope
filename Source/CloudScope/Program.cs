using System;
using System.Diagnostics;
using CloudScope;

string lasFile = args.Length > 0
    ? args[0]
    : @"C:\Users\primu\OneDrive\BorderEye\data\jeli_parkolo.las";

//string lasFile = args.Length > 0
//    ? args[0]
//    : @"D:\Personal\OneDrive\Ut1_colorized.las";

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

long total = maxPoints > 0 ? Math.Min(maxPoints, reader.PointCount) : reader.PointCount;
var points = new PointData[total];

double cx = (hdr.MinX + hdr.MaxX) * 0.5;
double cy = (hdr.MinY + hdr.MaxY) * 0.5;
double cz = (hdr.MinZ + hdr.MaxZ) * 0.5;
double spanZ = hdr.MaxZ - hdr.MinZ;

// Some files store 8-bit colors (0-255) in 16-bit fields — detect from first 1000 points.
float colorScale = 1.0f / 65535.0f;
ushort colorMax = 0;
bool colorScaleSet = !hasColor; // skip detection for non-color formats

long loaded = 0;
int lastPct = -1;

foreach (var pt in reader.GetPoints())
{
    if (loaded >= total) break;

    // Inline color scale detection from first 1000 points
    if (!colorScaleSet)
    {
        if (loaded < 1000)
        {
            if (pt.R > colorMax) colorMax = pt.R;
            if (pt.G > colorMax) colorMax = pt.G;
            if (pt.B > colorMax) colorMax = pt.B;
        }
        else
        {
            if (colorMax > 0 && colorMax <= 255)
            {
                colorScale = 1.0f / 255.0f;
                Console.WriteLine("\nInfo: Detected 8-bit colors (0-255). Scaling corrected.");
            }
            colorScaleSet = true;
        }
    }

    ref PointData p = ref points[loaded];
    p.X = (float)(pt.X - cx);
    p.Y = (float)(pt.Y - cy);
    p.Z = (float)(pt.Z - cz);

    if (hasColor)
    {
        p.R = pt.R * colorScale;
        p.G = pt.G * colorScale;
        p.B = pt.B * colorScale;
    }
    else
    {
        float t = spanZ > 0 ? (float)((pt.Z - hdr.MinZ) / spanZ) : 0.5f;
        t = Math.Clamp(t, 0f, 1f);
        p.R = t;
        p.G = 1f - MathF.Abs(2f * t - 1f);
        p.B = 1f - t;
    }

    loaded++;

    int pct = (int)(loaded * 100L / total);
    if (pct / 5 != lastPct / 5)
    {
        lastPct = pct;
        Console.Write($"\rLoading {pct,3}%");
    }
}

sw.Stop();
Console.WriteLine($"\rLoaded : {loaded:N0} points  ({sw.Elapsed.TotalSeconds:F1} s)");

// ── Fisher-Yates shuffle so first-K points give uniform spatial coverage ────
// CLOD in the viewer draws only the first N points based on zoom level.
{
    var rng = new Random(42);
    for (long i = loaded - 1; i > 0; i--)
    {
        long j = (long)(rng.NextDouble() * (i + 1));
        (points[i], points[j]) = (points[j], points[i]);
    }
    Console.WriteLine("Shuffled  : points reordered for CLOD");
}

// ── Compute cloud radius for initial camera fit ──────────────────────────────
float rangeX = (float)(hdr.MaxX - hdr.MinX) * 0.5f;
float rangeY = (float)(hdr.MaxY - hdr.MinY) * 0.5f;
float rangeZ = (float)(hdr.MaxZ - hdr.MinZ) * 0.5f;
float radius = MathF.Sqrt(rangeX * rangeX + rangeY * rangeY + rangeZ * rangeZ);

Console.WriteLine($"Cloud radius: {radius:F1} m");
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
using var viewer = new PointCloudViewer(1600, 900);
viewer.LoadPointCloud(points, radius);
viewer.SetLasFilePath(lasFile);
viewer.Run();

return 0;

