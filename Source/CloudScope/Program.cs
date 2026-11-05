using CloudScope;
using System.Globalization;

const string usage = "Usage: CloudScope [path.las] [max-points; 0 = all]";
if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine(usage);
    return 0;
}
int initialLimit = 0;
if (args.Length > 2 || (args.Length > 1 &&
    (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out initialLimit) || initialLimit < 0)))
{
    Console.Error.WriteLine(usage);
    return 1;
}
if (args.Length > 0 && !File.Exists(args[0]))
{
    Console.Error.WriteLine($"File not found: {args[0]}");
    return 1;
}

Console.WriteLine("CloudScope viewer");
Console.WriteLine();
Console.WriteLine("Camera controls:");
Console.WriteLine("  Left drag      - Orbit");
Console.WriteLine("  Right drag     - Pan  (clicked point stays under cursor)");
Console.WriteLine("  Scroll         - Zoom (depth-aware)");
Console.WriteLine("  W/A/S/D/Q/E    - FPS navigation");
Console.WriteLine("  Escape         - Exit");
Console.WriteLine();
Console.WriteLine("Command line:");
Console.WriteLine("  OPEN <path> [max-points]    - Load a .las point cloud");
Console.WriteLine("  OPENPLY <path> [max-points] - Load ASCII or binary PLY vertices");
Console.WriteLine("  OPENXYZ <path> [max-points] - Load XYZ text points");
Console.WriteLine("  OPENPTS <path> [max-points] - Load PTS text points");
Console.WriteLine("  INDEX <path> [dir] [Source] - Index a cloud of any size; Source keeps LAS record numbers");
Console.WriteLine("  OPENSTORE <store dir>       - Stream an indexed cloud straight off disk");
Console.WriteLine("  SCRIPT <path>               - Run a file of commands, one per line");
Console.WriteLine("  SETVAR ? *                  - List the system variables");
Console.WriteLine("  Enter / Space               - Submit or repeat the last command");
Console.WriteLine("  Up / Down                   - Browse command history");
Console.WriteLine("  Tab                         - Complete keywords and command names");
Console.WriteLine("  Escape                      - Cancel the active command");
Console.WriteLine("  \u0027COMMAND                    - Run a transparent command inside another");
Console.WriteLine("  F1 / F2                     - Help / expanded command history");
Console.WriteLine("  HELP / HELP <command>       - List every command, or explain one");
Console.WriteLine();

using var viewer = ViewerHostFactory.Create(1600, 900);
if (args.Length > 0) Console.WriteLine(viewer.OpenPointCloud(args[0], initialLimit));
viewer.Run();

return 0;
