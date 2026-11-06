# CloudScope parancs-nyilvántartás

Ez a fájl a futásidejű parancstáblából készül. Újragenerálás a repó gyökeréből:

```bash
dotnet run --project Source/CloudScope.CommandChecks -- --write-inventory Docs/CommandInventory.md
```

Összesen **79 parancs**, aliasokkal együtt **100 név**.
A `COMMANDS` és a `HELP` ugyanebből a regisztrációból dolgozik. A `COVERAGE` a viewer API statikus elérhetőségét méri; a működés ellenőrzését a CommandChecks végzi.

Az argumentumokat szóköz választja el. A szóközt tartalmazó útvonalat vagy egy argumentumként kezelt nevet idézőjelbe kell tenni, például `OPENXYZ "scan one.xyz" 5000` vagy `LABEL "Tree 1" 7`. Külön útvonalbekéréskor a teljes begépelt válasz az útvonal.

A parancsok és a szkriptek közös futtatót használnak. A szkript üres sora az aktív bekérés alapértékét fogadja el; nyugalmi állapotban nem ismétel parancsot. A beágyazott szkript a hívó folytatása előtt fut le. Ismeretlen parancs vagy érvénytelen numerikus válasz megszakítja a szkriptet. Egy futtatás legfeljebb 100 000 sort dolgoz fel.

## File (20)

| Parancs | Alias | Hatókör | Szintaxis | Művelet |
| --- | --- | --- | --- | --- |
| `ADDSTORE` | – | Viewer | `ADDSTORE <store directory>` | Adds another point tile store as a layer beside the open ones. |
| `EXPORT` | – | Document | `EXPORT [PlyBinary/PlyAscii/Xyz/Pts/Csv] <path>` | Exports the filtered resident sample in its original coordinate frame. |
| `EXPORTOBJ` | – | Document | `EXPORTOBJ <path>` | Saves the reconstructed surface in the original coordinate frame. |
| `INDEX` | – | Application | `INDEX <las> [directory] [Source] [CHunk n] [Grid n] [MinPoints n] [SCratch dir]` | Indexes a LAS or LAZ file into a point tile store. |
| `LAYER` | LA | Viewer | `LAYER [List/ON/OFf/Close] <name>` | Lists the open point clouds and layers, or turns one on, off or closed. |
| `LOADLABELS` | – | Document | `LOADLABELS [Transfer] [path]` | Loads labels from a JSON file. |
| `LOADPOLYLINES` | – | Viewer | `LOADPOLYLINES <path>` | Loads planar polylines from a versioned JSON document. |
| `OPEN` | – | Viewer | `OPEN <path> [max points]` | Loads a LAS or compressed LAZ point cloud into memory. |
| `OPENE57` | – | Viewer | `OPENE57 <path> [max points]` | Loads E57 numeric bitpack scans with pose transforms and checksum validation. |
| `OPENPLY` | – | Viewer | `OPENPLY <path> [max points]` | Loads ASCII or little-endian binary PLY vertices into memory. |
| `OPENPROJECT` | – | Viewer | `OPENPROJECT <path>` | Restores a complete project after validating its sources. |
| `OPENPTS` | – | Viewer | `OPENPTS <path> [max points]` | Loads PTS text points with optional intensity and RGB. |
| `OPENPTX` | – | Viewer | `OPENPTX <path> [max points]` | Loads PTX scans using their scanner transforms. |
| `OPENSTORE` | – | Viewer | `OPENSTORE <store directory>` | Streams an indexed point tile store straight off disk. |
| `OPENXYZ` | – | Viewer | `OPENXYZ <path> [max points]` | Loads an XYZ text point cloud into memory. |
| `RECOVER` | – | Viewer | `RECOVER [path]` | Restores the newest abandoned automatic recovery snapshot, or a supplied snapshot. |
| `SAVEAS` | – | Viewer | `SAVEAS <path>` | Saves the complete project under a new name. |
| `SAVELABELS` | – | Document | `SAVELABELS [Las] [path]` | Writes labels to JSON, or class codes into a copy of the LAS. |
| `SAVEPOLYLINES` | – | Viewer | `SAVEPOLYLINES <path>` | Saves every planar polyline to a versioned JSON document. |
| `SAVEPROJECT` | SAVE | Viewer | `SAVEPROJECT [path]` | Saves the complete project, including drawings, labels and views. |

## Edit (13)

| Parancs | Alias | Hatókör | Szintaxis | Művelet |
| --- | --- | --- | --- | --- |
| `3DPOLY` | – | Viewer | `3DPOLY <first point> <next point>... [Close/Undo]` | Draws a straight-segment 3D polyline with object and axis snapping. |
| `CANCEL` | ESC | Viewer | `CANCEL` | Cancels the active command, selection or background processing. |
| `CONFIRM` | – | Document | `CONFIRM` | Applies the active selection to the current label. |
| `FIT` | – | Document | `FIT [Ground]` | Shrinks the selection volume onto the points inside it. |
| `MOVE` | M | Document | `MOVE [X/Y/Z] <dx,dy,dz> \| <base point> <second point>` | Moves the selection volume by a displacement or between two points. |
| `ORTHO` | – | Viewer | `ORTHO [ON/OFF/Toggle] <toggle>` | Turns axis locking for point input on or off. |
| `PEDIT` | PE | Viewer | `PEDIT [Close/Join/Open/Reverse/Width]` | Edits the selected planar polyline. |
| `PLINE` | PL | Viewer | `PLINE <start point> <next point>... [Arc/Close/Halfwidth/Length/Undo/Width]` | Draws a planar polyline made of connected line and tangent arc segments. |
| `REDO` | – | Viewer | `REDO [<number>]` | Reapplies commands stepped back with UNDO. |
| `ROTATE` | RO | Document | `ROTATE [X/Y/Z] <angle in degrees>` | Rotates the selection volume about a world axis. |
| `SCALE` | SC | Document | `SCALE <factor> \| Reference <current> <new>` | Scales the selection volume by a factor. |
| `SELECT` | – | Document | `SELECT [Box/Sphere/Cylinder/Undo/CONFirm/CANcel/Fit/Ground]` | Draws a selection volume and applies it to the current label. |
| `UNDO` | U | Viewer | `UNDO [<number>/Mark/Back]` | Steps back through completed commands. |

## Label (10)

| Parancs | Alias | Hatókör | Szintaxis | Művelet |
| --- | --- | --- | --- | --- |
| `CLEARLABELS` | – | Document | `CLEARLABELS` | Removes every label from the cloud. |
| `GROUNDSEG` | – | Document | `GROUNDSEG [resolution] [rigidness] [timeStep] [threshold] [iterations] [ON/OFF]` | Separates terrain points with the CSF cloth-simulation ground filter. |
| `INSTANCE` | – | Document | `INSTANCE <id> \| CLear` | Sets or clears the instance id new selections are given. |
| `LABEL` | – | Document | `LABEL <name> [instance id]` | Sets the label (and optionally the instance) new selections are given. |
| `LABELDEF` | – | Viewer | `LABELDEF <name> <class 0-255> [Color r,g,b] \| List \| DElete <name>` | Defines, colours, lists or deletes a label and its LAS class code. |
| `LABELMODE` | L | Document | `LABELMODE` | Toggles between label mode and navigation mode. |
| `LABELS` | – | Viewer | `LABELS` | Shows or hides the label registry window. |
| `NAVIGATE` | – | Viewer | `NAVIGATE` | Switches to navigation mode. |
| `TREESEG` | – | Document | `TREESEG <seed point>` | Segments one terrestrial/SLAM tree from a picked trunk seed. |
| `UNLABEL` | – | Document | `UNLABEL` | Removes the labels of every point inside the active selection volume. |

## View (15)

| Parancs | Alias | Hatókör | Szintaxis | Művelet |
| --- | --- | --- | --- | --- |
| `COLORBY` | – | Document | `COLORBY [Rgb/Height/Class/Intensity/ReTurn/CLear]` | Colours the cloud by one of its attributes. |
| `FILTER` | – | Document | `FILTER [Class/Intensity/Return/Z/CLear] <values>` | Shows only the points matching an attribute filter. |
| `MEASURE` | MEAS | Document | `MEASURE [Distance/Area/List/Remove/Export] <name> <points...> [Close]` | Stores named distance, height, slope or horizontal area measurements; exports CSV/DXF. |
| `ORBIT` | OR | Viewer | `ORBIT <azimuth,elevation> \| Reset` | Orbits the camera by an angle, or resets the orbit. |
| `PAN` | P | Viewer | `PAN <base point> <second point> \| Displacement <dx,dy>` | Repositions the view in the active viewport. |
| `PIVOT` | – | Viewer | `PIVOT <x,y,z> \| Screen <x,y> \| Extents` | Sets the point the view orbits around. |
| `POINTSIZE` | – | Viewer | `POINTSIZE <size> \| + \| -` | Sets the on-screen size of a point, in pixels. |
| `PROJECTION` | – | Viewer | `PROJECTION [Perspective/PArallel]` | Switches between perspective and parallel projection. |
| `RESET` | – | Viewer | `RESET` | Resets the viewer to its initial view and state. |
| `SURFACE` | – | Viewer | `SURFACE [ON/OFf/CLear]` | Shows, hides or clears the surface; Clear also cancels reconstruction. |
| `THIN` | – | Document | `THIN <keep percentage 1..100>` | Shows a repeatable sample of the filtered resident cloud; 100 restores all points. |
| `VIEW` | V | Viewer | `VIEW [Front/BAck/Left/Right/Top/Bottom/Isometric/Save/Restore/LIst/DElete]` | Sets a standard view, or saves and restores a named one. |
| `VPORTS` | VP | Viewer | `VPORTS [Single/2/3/4/5/6/7/8/9/Plan/PRevious] [Vertical/Horizontal] [view]` | Splits the drawing area into viewports. |
| `XSECTION` | XS | Document | `XSECTION <first point> <second point> [width] \| [New/Width/Flip/View/List/Activate/Rename/Export/CLear]` | Creates and displays a finite vertical point-cloud cross-section. |
| `ZOOM` | Z | Viewer | `ZOOM [All/Center/Dynamic/Extents/Object/PRevious/RealTime/Scale/Window] \| <corner>` | Changes magnification in the active viewport. |

## Inquiry (10)

| Parancs | Alias | Hatókör | Szintaxis | Művelet |
| --- | --- | --- | --- | --- |
| `ATTRIBUTES` | – | Document | `ATTRIBUTES [All/Class/Intensity/Return/Z]` | Reports the distribution of an attribute across the cloud. |
| `COMMANDLINE` | – | Viewer | `COMMANDLINE [On/Off/Toggle/Float/Dock]` | Shows, hides, floats or docks the command window. |
| `EXPLORER` | EX | Viewer | `EXPLORER [On/Off/Toggle/Left/Right]` | Shows, hides or docks the explorer of open point clouds and scene objects. |
| `HISTORY` | – | Viewer | `HISTORY` | Shows or hides the expanded command history window. |
| `LABELSTAT` | – | Document | `LABELSTAT` | Reports how many points carry each label. |
| `PROPERTIES` | PR | Viewer | `PROPERTIES [On/Off/Toggle]` | Shows or hides the properties palette of the explorer selection. |
| `RIBBON` | – | Viewer | `RIBBON [On/Off/Toggle]` | Shows the ribbon's tool panels, or minimises it to its tabs. |
| `STATUS` | – | Viewer | `STATUS` | Reports what the viewer is currently showing and doing. |
| `STOREINFO` | – | Viewer | `STOREINFO` | Reports the structure of the open point tile stores. |
| `TIME` | – | Viewer | `TIME` | Reports frame rate and point throughput. |

## Settings (4)

| Parancs | Alias | Hatókör | Szintaxis | Művelet |
| --- | --- | --- | --- | --- |
| `GETVAR` | – | Viewer | `GETVAR <name>` | Reports the value of a system variable. |
| `GRAPHICSCONFIG` | – | Viewer | `GRAPHICSCONFIG` | Reports the rendering backend and how to select one. |
| `POINTCLOUDCONFIG` | – | Viewer | `POINTCLOUDCONFIG [Frame <points>/Resident <points>/Show]` | Shows or sets the per-frame and resident point budgets. |
| `SETVAR` | – | Viewer | `SETVAR <name> <value> \| ? [pattern]` | Lists or changes a system variable. |

## Utility (7)

| Parancs | Alias | Hatókör | Szintaxis | Művelet |
| --- | --- | --- | --- | --- |
| `API` | – | Viewer | `API [ON/OFf] [port]` | Starts or stops the authenticated local command HTTP API. |
| `COMMANDS` | – | Application | `COMMANDS` | Lists every registered command, alias, scope, and usage. |
| `COVERAGE` | – | Application | `COVERAGE` | Reports which viewer capabilities no command can reach. |
| `HELP` | ? | Application | `HELP [command]` | Lists the commands, or explains one of them. |
| `QUIT` | – | Application | `QUIT` | Closes the viewer. |
| `RECONSTRUCT` | – | Document | `RECONSTRUCT <neighbors >= 3> <maximum edge length; 0 = auto>` | Builds an approximate surface using PCA normals and greedy projection. |
| `SCRIPT` | SCR | Application | `SCRIPT <path>` | Runs a file of commands, one per line. |

A folyamatos egér- és billentyűgesztusok bemeneti események; a megfelelő műveletek parancsból is elérhetők. A rendszer részletei: [CommandSystem.md](CommandSystem.md).
