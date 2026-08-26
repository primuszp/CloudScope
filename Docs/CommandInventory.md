# CloudScope parancs-nyilvántartás

A program **teljes parancsfelülete**, közvetlenül a kódból kinyerve. A rendszer felépítését a
[CommandSystem.md](CommandSystem.md), a hozzá vezető tervet a
[CommandSystemPlan.md](CommandSystemPlan.md) írja le.

Ez a dokumentum ma már nem az egyetlen őre a lefedettségnek: a `COVERAGE` parancs (és a
`Source/CloudScope.CommandChecks` futtatása) a fordított kódból állapítja meg, mely
viewer-képességhez nem vezet parancs. **Jelenleg 51 az 51-ből elérhető.**

Összesen **52 parancs**, 34 aliasszal, hét csoportban.


## Fájl és adat (7)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `ADDSTORE` | – | `ADDSTORE <store directory>` | Adds another point tile store as a layer beside the open ones. |
| `INDEX` | – | `INDEX <las> [directory] [Source] [CHunk n] [Grid n] [MinPoints n] [SCratch dir]` | Indexes a LAS file into a point tile store of any size. |
| `LAYER` | LA | `LAYER [List/ON/OFf/Close] <name>` | Lists layers, or turns one on, off or closed. |
| `LOADLABELS` | QLLOAD | `LOADLABELS [path]` | Loads labels from a JSON file. |
| `OPEN` | – | `OPEN <path> [max points]` | Loads a LAS or LAZ point cloud into memory. |
| `OPENSTORE` | – | `OPENSTORE <store directory>` | Streams an indexed point tile store straight off disk. |
| `SAVELABELS` | QLSAVE | `SAVELABELS [Las] [path]` | Writes labels to JSON, or class codes into a copy of the LAS. |

## Szerkesztés (10)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `CANCEL` | ESC, ESCAPE | `CANCEL` | Cancels the active command or selection. |
| `CONFIRM` | ENTER | `CONFIRM` | Applies the active selection to the current label. |
| `FIT` | – | `FIT [Ground]` | Shrinks the selection volume onto the points inside it. |
| `FITGROUND` | – | `FITGROUND` | Fits the selection volume, keeping its base on the ground. |
| `MOVE` | M | `MOVE [X/Y/Z] <dx,dy,dz> \| <base point> <second point>` | Moves the selection volume by a displacement or between two points. |
| `REDO` | – | `REDO [<number>]` | Reapplies commands stepped back with UNDO. |
| `ROTATE` | RO | `ROTATE [X/Y/Z] <angle in degrees>` | Rotates the selection volume about a world axis. |
| `SCALE` | SC | `SCALE <factor> \| Reference <current> <new>` | Scales the selection volume by a factor. |
| `SELECT` | SEL | `SELECT [Box/Sphere/Cylinder/Undo/CONFirm/CANcel/Fit/GroundFit]` | Draws a selection volume and applies it to the current label. |
| `UNDO` | U | `UNDO [<number>/Mark/Back]` | Steps back through completed commands. |

## Címkézés (8)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `CLEARLABELS` | – | `CLEARLABELS` | Removes every label from the cloud. |
| `INSTANCE` | INST | `INSTANCE <id> \| CLear` | Sets or clears the instance id new selections are given. |
| `LABEL` | – | `LABEL <name> [instance id]` | Sets the label (and optionally the instance) new selections are given. |
| `LABELDEF` | – | `LABELDEF <name> <class 0-255> [Color r,g,b] \| List \| DElete <name>` | Defines, colours, lists or deletes a label and its LAS class code. |
| `LABELMODE` | L | `LABELMODE` | Toggles between label mode and navigation mode. |
| `LABELS` | – | `LABELS` | Shows or hides the label registry window. |
| `NAVIGATE` | NAV, N | `NAVIGATE` | Switches to navigation mode. |
| `UNLABEL` | ERASE | `UNLABEL` | Removes the labels of every point inside the active selection volume. |

## Nézet (11)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `COLORBY` | COLOR, CB | `COLORBY [Rgb/Height/Class/Intensity/ReTurn/CLear]` | Colours the cloud by one of its attributes. |
| `FILTER` | FI | `FILTER [Class/Intensity/Return/Z/CLear] <values>` | Shows only the points matching an attribute filter. |
| `ORBIT` | OR | `ORBIT <azimuth,elevation> \| Reset` | Orbits the camera by an angle, or resets the orbit. |
| `PAN` | P | `PAN <dx,dy> \| Point <base> <target>` | Pans the view by a pixel offset or between two points. |
| `PIVOT` | – | `PIVOT <x,y,z> \| Screen <x,y> \| Extents` | Sets the point the view orbits around. |
| `POINTSIZE` | PSIZE | `POINTSIZE <size> \| + \| -` | Sets the on-screen size of a point, in pixels. |
| `PROJECTION` | PROJ, PERSPECTIVE | `PROJECTION [Perspective/PArallel]` | Switches between perspective and parallel projection. |
| `RESET` | – | `RESET` | Resets the viewer to its initial view and state. |
| `VIEW` | V | `VIEW [Front/BAck/Left/Right/Top/Bottom/Isometric/Save/Restore/LIst/DElete]` | Sets a standard view, or saves and restores a named one. |
| `VPORTS` | VP | `VPORTS [Single/Two/Plan/PRevious] [Vertical/Horizontal] [view]` | Splits the drawing area into viewports. |
| `ZOOM` | Z | `ZOOM [All/Center/Extents/Object/Window] \| <scale> \| <nX> \| <corner> <corner>` | Zooms the view by a scale factor, to extents, or into a window. |

## Lekérdezés (7)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `ATTRIBUTES` | ATTRS | `ATTRIBUTES [All/Class/Intensity/Return/Z]` | Reports the distribution of an attribute across the cloud. |
| `HISTORY` | – | `HISTORY` | Shows or hides the expanded command history window. |
| `HOSTSTATUS` | – | `HOSTSTATUS` | Reports the shell's own status line. |
| `LABELSTAT` | – | `LABELSTAT` | Reports how many points carry each label. |
| `STATUS` | – | `STATUS` | Reports what the viewer is currently showing and doing. |
| `STOREINFO` | – | `STOREINFO` | Reports the structure of the open point tile stores. |
| `TIME` | – | `TIME` | Reports frame rate and point throughput. |

## Beállítás (4)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `GETVAR` | – | `GETVAR <name>` | Reports the value of a system variable. |
| `GRAPHICSCONFIG` | 3DCONFIG | `GRAPHICSCONFIG` | Reports the rendering backend and how to select one. |
| `POINTCLOUDCONFIG` | PTCONFIG | `POINTCLOUDCONFIG [Frame <points>/Resident <points>/Show]` | Shows or sets the per-frame and resident point budgets. |
| `SETVAR` | SET | `SETVAR <name> <value> \| ? [pattern]` | Lists or changes a system variable. |

## Segédparancsok (5)

| Parancs | Alias | Szintaxis | Mit csinál |
| --- | --- | --- | --- |
| `COVERAGE` | – | `COVERAGE` | Reports which viewer capabilities no command can reach. |
| `HELP` | ? | `HELP [command]` | Lists the commands, or explains one of them. |
| `HOSTRESET` | – | `HOSTRESET` | Reinitialises the embedded viewer host. |
| `QUIT` | EXIT | `QUIT` | Closes the viewer. |
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
