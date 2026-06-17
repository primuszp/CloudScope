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
Console.WriteLine("  OPEN <path> [max-points] - Load a .las/.laz point cloud");
Console.WriteLine("  Enter / Space            - Submit or repeat the last command");
Console.WriteLine("  Up / Down                - Browse command history");
Console.WriteLine("  Escape                   - Cancel the active command");
Console.WriteLine("  Commands                 - OPEN, SELECT, ZOOM, VIEW, PROJECTION, POINTSIZE,");
Console.WriteLine("                             LABEL, SAVELABELS, LOADLABELS, UNDO, HELP");
Console.WriteLine();

using var viewer = ViewerHostFactory.Create(1600, 900);
viewer.Run();

return 0;
