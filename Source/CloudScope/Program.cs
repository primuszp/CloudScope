using CloudScope;

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
Console.WriteLine("  OPEN <path> [max-points]    - Load a .las/.laz point cloud");
Console.WriteLine("  INDEX <path> [dir] [Source] - Index a cloud of any size; Source keeps LAS record numbers");
Console.WriteLine("  OPENSTORE <store dir>       - Stream an indexed cloud straight off disk");
Console.WriteLine("  Enter / Space               - Submit or repeat the last command");
Console.WriteLine("  Up / Down                   - Browse command history");
Console.WriteLine("  Tab                         - Complete keywords and command names");
Console.WriteLine("  Escape                      - Dismiss the list, then cancel the command");
Console.WriteLine("  F1 / F2                     - Help / expanded command history");
Console.WriteLine("  HELP                        - List every registered command");
Console.WriteLine();

using var viewer = ViewerHostFactory.Create(1600, 900);
viewer.Run();

return 0;
