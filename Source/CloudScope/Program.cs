using System;
using System.Diagnostics;
using CloudScope;

//string lasFile = args.Length > 0
//    ? args[0]
//    : @"D:\Personal\OneDrive\BorderEye\data\jeli_parkolo.las";

string lasFile = args.Length > 0
    ? args[0]
    : @"D:\Personal\OneDrive\Ut1_colorized.las";

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


// ── Compute cloud radius for initial camera fit ──────────────────────────────
float rangeX = (float)(hdr.MaxX - hdr.MinX) * 0.5f;
float rangeY = (float)(hdr.MaxY - hdr.MinY) * 0.5f;
float rangeZ = (float)(hdr.MaxZ - hdr.MinZ) * 0.5f;
float radius = MathF.Sqrt(rangeX * rangeX + rangeY * rangeY + rangeZ * rangeZ);

Console.WriteLine($"Cloud radius: {radius:F1} m");
Console.WriteLine();
Console.WriteLine("Controls:");
Console.WriteLine("  Left Mouse     - Orbit");
Console.WriteLine("  Right Mouse    - Pan");
Console.WriteLine("  Scroll Wheel   - Zoom (depth-aware)");
Console.WriteLine("  Space          - Toggle Perspective / Orthographic");
Console.WriteLine("  W/A/S/D/Q/E    - FPS Navigation");
Console.WriteLine("  Num1/3/7/5     - Front / Right / Top / Isometric view (animated)");
Console.WriteLine("  F              - Focus on point under cursor");
Console.WriteLine("  R              - Reset view (animated)");
Console.WriteLine("  +/-            - Point size");
Console.WriteLine("  Escape         - Exit");
Console.WriteLine();

// ── Launch viewer ────────────────────────────────────────────────────────────
using var viewer = new PointCloudViewer(1600, 900);
viewer.LoadPointCloud(points, radius);
viewer.Run();

return 0;

