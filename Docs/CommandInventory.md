# CloudScope parancs-nyilvántartás

A program teljes parancsfelületének olvasható áttekintése. A futásidejű, kanonikus lista a
`COMMANDS` parancs kimenete: ugyanabból a regisztrációs táblából készül, amely a parancsokat
végrehajtja. A rendszer felépítését a
[CommandSystem.md](CommandSystem.md), a hozzá vezető tervet a
[CommandSystemPlan.md](CommandSystemPlan.md) írja le.

Ez a dokumentum ma már nem az egyetlen őre a lefedettségnek: a `COVERAGE` parancs (és a
`Source/CloudScope.CommandChecks` futtatása) a fordított kódból állapítja meg, mely
viewer-képességhez nem vezet parancs. **Jelenleg 87 a 87-ből elérhető.**

Összesen **60 parancs** érhető el (77 név az aliasokkal). A `Source/CloudScope.CommandChecks`
futtatása ellenőrzi az aktuális neveket, aliasokat, az angol/ASCII parancsmetaadatokat és a
menühivatkozásokat. Minden parancsnak egy kanonikus neve van; az alias rövid, szándékos
gyorsírás, és a CommandChecks kikényszeríti, hogy egyetlen név se ütközzön. Egy
kanonikus `ViewerCommandDispatcher` futtatja mindet; az Avalonia-projekt UI-réteg, saját
parancs-implementáció nélkül.


## Fájl és adat (9)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `ADDSTORE` | – | `ADDSTORE <store directory>` | Adds another point tile store as a layer beside the open ones. |
| `INDEX` | – | `INDEX <las> [directory] [Source] [CHunk n] [Grid n] [MinPoints n] [SCratch dir]` | Indexes a LAS file into a point tile store of any size. |
| `LAYER` | LA | `LAYER [List/ON/OFf/Close] <name>` | Lists layers, or turns one on, off or closed. |
| `LOADLABELS` | – | `LOADLABELS [path]` | Loads labels from a JSON file. |
| `LOADPOLYLINES` | – | `LOADPOLYLINES <path>` | Loads versioned planar-polyline JSON and records the import for undo. |
| `OPEN` | – | `OPEN <path> [max points]` | Loads a LAS or LAZ point cloud into memory. |
| `OPENSTORE` | – | `OPENSTORE <store directory>` | Streams an indexed point tile store straight off disk. |
| `SAVELABELS` | – | `SAVELABELS [Las] [path]` | Writes labels to JSON, or class codes into a copy of the LAS. |
| `SAVEPOLYLINES` | – | `SAVEPOLYLINES <path>` | Saves every planar polyline to versioned JSON. |

## Szerkesztés (13)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `CANCEL` | ESC | `CANCEL` | Cancels the active command or selection. |
| `CONFIRM` | – | `CONFIRM` | Applies the active selection to the current label. |
| `FIT` | – | `FIT [Ground]` | Shrinks the selection volume onto the points inside it. |
| `MOVE` | M | `MOVE [X/Y/Z] <dx,dy,dz> \| <base point> <second point>` | Moves the selection volume by a displacement or between two points. |
| `ORTHO` | – | `ORTHO [ON/OFF] <toggle>` | Turns axis locking for point input on or off. |
| `3DPOLY` | – | `3DPOLY <first point> <next point>... [Close/Undo]` | Creates a non-planar chain of straight 3D segments. |
| `PLINE` | PL | `PLINE <start point> <next point>... [Arc/Close/Halfwidth/Length/Undo/Width]` | Creates one planar object from line and tangent-arc segments, with optional tapered widths. |
| `PEDIT` | PE | `PEDIT [Close/Join/Open/Reverse/Width]` | Edits the selected planar polyline. |
| `REDO` | – | `REDO [<number>]` | Reapplies commands stepped back with UNDO. |
| `ROTATE` | RO | `ROTATE [X/Y/Z] <angle in degrees>` | Rotates the selection volume about a world axis. |
| `SCALE` | SC | `SCALE <factor> \| Reference <current> <new>` | Scales the selection volume by a factor. |
| `SELECT` | – | `SELECT [Box/Sphere/Cylinder/Undo/CONFirm/CANcel/Fit/Ground]` | Draws a selection volume and applies it to the current label. |
| `UNDO` | U | `UNDO [<number>/Mark/Back]` | Steps back through completed commands. |

## Címkézés (10)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `CLEARLABELS` | – | `CLEARLABELS` | Removes every label from the cloud. |
| `GROUNDSEG` | – | `GROUNDSEG [resolution] [rigidness] [timeStep] [threshold] [iterations] [ON/OFF]` | Separates terrain points with the CSF cloth-simulation ground filter. |
| `INSTANCE` | – | `INSTANCE <id> \| CLear` | Sets or clears the instance id new selections are given. |
| `LABEL` | – | `LABEL <name> [instance id]` | Sets the label (and optionally the instance) new selections are given. |
| `LABELDEF` | – | `LABELDEF <name> <class 0-255> [Color r,g,b] \| List \| DElete <name>` | Defines, colours, lists or deletes a label and its LAS class code. |
| `LABELMODE` | L | `LABELMODE` | Toggles between label mode and navigation mode. |
| `LABELS` | – | `LABELS` | Shows or hides the label registry window. |
| `NAVIGATE` | – | `NAVIGATE` | Switches to navigation mode. |
| `TREESEG` | – | `TREESEG <seed point>` | Segments one terrestrial/SLAM tree from a picked trunk seed. |
| `UNLABEL` | – | `UNLABEL` | Removes the labels of every point inside the active selection volume. |

## Nézet (12)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `COLORBY` | – | `COLORBY [Rgb/Height/Class/Intensity/ReTurn/CLear]` | Colours the cloud by one of its attributes. |
| `FILTER` | – | `FILTER [Class/Intensity/Return/Z/CLear] <values>` | Shows only the points matching an attribute filter. |
| `ORBIT` | OR | `ORBIT <azimuth,elevation> \| Reset` | Orbits the camera by an angle, or resets the orbit. |
| `PAN` | P | `PAN <base point> <second point> \| Displacement <dx,dy>` | Repositions the view in the active viewport. |
| `PIVOT` | – | `PIVOT <x,y,z> \| Screen <x,y> \| Extents` | Sets the point the view orbits around. |
| `POINTSIZE` | – | `POINTSIZE <size> \| + \| -` | Sets the on-screen size of a point, in pixels. |
| `PROJECTION` | – | `PROJECTION [Perspective/PArallel]` | Switches between perspective and parallel projection. |
| `RESET` | – | `RESET` | Resets the viewer to its initial view and state. |
| `VIEW` | V | `VIEW [Front/BAck/Left/Right/Top/Bottom/Isometric/Save/Restore/LIst/DElete]` | Sets a standard view, or saves and restores a named one. |
| `VPORTS` | VP | `VPORTS [Single/Two/Plan/PRevious] [Vertical/Horizontal] [view]` | Splits the drawing area into viewports. |
| `XSECTION` | XS | `XSECTION <first point> <second point> [width] \| [New/Width/Flip/View/List/CLear]` | Creates and displays a finite vertical point-cloud cross-section. |
| `ZOOM` | Z | `ZOOM [All/Center/Dynamic/Extents/Object/PRevious/RealTime/Scale/Window] \| <corner>` | Changes magnification in the active viewport. |

## Lekérdezés (7)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `ATTRIBUTES` | – | `ATTRIBUTES [All/Class/Intensity/Return/Z]` | Reports the distribution of an attribute across the cloud. |
| `HISTORY` | – | `HISTORY` | Shows or hides the expanded command history window. |
| `COMMANDLINE` | – | `COMMANDLINE [On/Off/Toggle/Float/Dock]` | Shows, hides, floats or docks the command window. |
| `LABELSTAT` | – | `LABELSTAT` | Reports how many points carry each label. |
| `STATUS` | – | `STATUS` | Reports what the viewer is currently showing and doing. |
| `STOREINFO` | – | `STOREINFO` | Reports the structure of the open point tile stores. |
| `TIME` | – | `TIME` | Reports frame rate and point throughput. |

## Beállítás (4)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `GETVAR` | – | `GETVAR <name>` | Reports the value of a system variable. |
| `GRAPHICSCONFIG` | – | `GRAPHICSCONFIG` | Reports the rendering backend and how to select one. |
| `POINTCLOUDCONFIG` | – | `POINTCLOUDCONFIG [Frame <points>/Resident <points>/Show]` | Shows or sets the per-frame and resident point budgets. |
| `SETVAR` | – | `SETVAR <name> <value> \| ? [pattern]` | Lists or changes a system variable. |

## Segédparancsok (5)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `COVERAGE` | – | `COVERAGE` | Reports which viewer capabilities no command can reach. |
| `COMMANDS` | – | `COMMANDS` | Lists every registered command, alias, scope, and usage. |
| `HELP` | ? | `HELP [command]` | Lists the commands, or explains one of them. |
| `QUIT` | – | `QUIT` | Closes the viewer. |
| `SCRIPT` | SCR | `SCRIPT <path>` | Runs a file of commands, one per line. |


## Rendszerváltozók

A `SETVAR` és a `GETVAR` a viewer állapotát nevesíti. A változó nem tárol semmit: olvasó- és
íróhivatkozás a már meglévő mezőre, ezért nem tud elcsúszni attól, amit a viewer valóban csinál.

| Változó | Mit ír le | Írható |
| --- | --- | --- |
| `PDSIZE` | pontméret képpontban | igen |
| `PERSPECTIVE` | perspektivikus (1) vagy párhuzamos (0) vetítés | igen |
| `COLORSOURCE` | színezés forrása | igen |
| `CLABEL`, `CINSTANCE` | az új kijelölés címkéje és példányazonosítója | igen |
| `PTMAX` | képkockánként kirajzolt pontok felső határa | igen |
| `PTRESIDENT` | GPU-n tartott pontok felső határa | igen |
| `VPORTLAYOUT`, `VIEWNAME` | nézetablak-elrendezés és nézet neve | nem |
| `SELMODE`, `SELTOOL` | mód és aktív kijelölőeszköz | nem |
| `RENDERBACKEND` | a futó renderelő háttér | nem |
| `SOURCENAME`, `LOADEDPOINTS`, `VISIBLEPOINTS` | a megnyitott felhő | nem |
| `FPS`, `LABELCOUNT` | képkockasebesség, címkézett pontok száma | nem |

## Ami szándékosan nem parancs

A folyamatos gesztus bemeneti esemény marad, nem parancs — ahogy az AutoCAD-ben is együtt él a
`PAN` parancs és a középgomb húzása:

- bal gomb húzása: orbit
- jobb gomb húzása: pásztázás
- görgő: mélységérzékeny zoom
- W/A/S/D/Q/E: szabad mozgás
- fogópont húzása: a kijelölő test alakítása

Mindegyikhez van parancs-megfelelő is (`ORBIT`, `PAN`, `ZOOM`, `MOVE`, `ROTATE`, `SCALE`),
így minden művelet elérhető szkriptből is.
