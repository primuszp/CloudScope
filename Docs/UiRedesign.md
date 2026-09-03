# CloudScope UI Redesign

Two shells, one command engine: the Avalonia workspace (native-leaning, macOS
first) and the OpenTK GameWindow viewer (fully panel-rendered ImGui UI). Both are
adapters over `CloudScope.Core` — the redesign adds no second source of truth.

## 1. Current state — findings

### Shared / Core
* `CommandRuntime` already carries the AutoCAD-grade pieces (`PromptOptions`,
  `Keyword` with capital-letter abbreviations, `DefaultKeyword`,
  `AllowArbitraryInput`, transparent commands, repeat-on-Enter). **None of this
  reaches either UI.** Both shells render `CurrentPrompt` as flat text; the
  keyword list in `[...]` is never clickable, never completable.
* `KnownCommandNames` is exposed by every executor and used by nobody →
  no autocomplete anywhere.
* `CommandLineSession.History` is a `List<string>` of already-formatted strings.
  It cannot distinguish echoed input / command output / error / prompt, so no UI
  can style them differently.

### Avalonia (`MainWindow.axaml`)
* Large parts are a static mockup, not a UI: the Properties grid values
  (`None`, `Point Cloud`, `Classification`, `1.0 px`), the status bar
  `SNAP GRID ORTHO POLAR`, the `[ TOP ] / 2D WIREFRAME` badge, the fake ViewCube
  and the axis tripod are hardcoded `TextBlock`s bound to nothing.
* The command surface has ~25 commands; the menu/toolbar reaches ~12. Missing
  entirely: `ZOOM`, `VIEW`, `VPORTS`, `FILTER`, `COLORBY`, `ATTRIBUTES`,
  `POINTSIZE`, `LABEL`, `INSTANCE`, `LABELDEF`, `SAVELABELS LAS`, `LOADLABELS`,
  `CLEARLABELS`, `UNDO`.
* `FontFamily="Segoe UI, Arial"` and `Cascadia Mono, Consolas` are Windows fonts;
  on macOS both fall back arbitrarily. All colours are hardcoded dark — the app
  ignores the system appearance and accent colour.
* The XAML menu advertises `Ctrl+O` while the native macOS menu binds `Meta+O`.
  The native menu has only *Host* and *Viewer* groups — not the mandatory macOS
  App/File/Edit/View/Window/Help order.
* Space submits the command box unconditionally, so a quoted path or any
  multi-word argument cannot be typed. AutoCAD suppresses space-as-Enter while a
  prompt accepts arbitrary text — `PromptOptions.AllowArbitraryInput` already
  tells us when.
* `RefreshHistory()` rebuilds the whole history into a `TextBox` on every message
  — O(n) per line and it fights the user's scroll position.

### GameWindow (`CommandLineOverlay`)
* Command line is a fixed 86 px strip showing exactly **2** history lines, not
  resizable, no F2 expanded history.
* No properties/inspector panel, no status bar, no tool state feedback other than
  one `TextDisabled` line in the menu bar.
* Menus expose a different, smaller subset than Avalonia's menus — the two
  shells have drifted.

## 2. Core work (do this first — both shells depend on it)

**2.1 `CommandLineEntry`** — replace `List<string>` history with
`record CommandLineEntry(CommandEntryKind Kind, string Text)`,
`Kind ∈ {Echo, Prompt, Output, Error, Banner}`. `CommandLineSession` classifies
on `Submit` (`CommandStatus.Failed`→`Error`, `Cancelled`→`Error`, message→`Output`).
Keep a `string Text` view so existing callers compile.

**2.2 `CommandLineSession.ActiveOptions`** — surface the live `PromptOptions`
(runtime already has `_activePrompt`; expose it through `ICommandExecutor`).
This is what makes keywords clickable and drives space-vs-enter policy.

**2.3 `CommandCompletionSource`** — new Core type. Given the typed prefix and
`ActiveOptions`, returns ranked candidates: active-prompt keywords first, then
command names, then aliases, each with its `Display`/`Abbreviation`. One
implementation, both shells render it.

**2.4 `SubmitPolicy`** — `bool SpaceSubmits => ActiveOptions?.AllowArbitraryInput != true`.
Fixes the Avalonia space bug and gives ImGui the same rule.

**2.5 `ViewerStatusSnapshot`** — a single struct the viewer publishes on change
(`Mode`, `ActiveToolType`, `CurrentLabel`, `CurrentInstanceId`, `PointCount`,
`VisibleCount`, `ColorSource`, `PointSize`, `IsPerspective`, `ViewportLayout`,
`Fps`). It replaces every hardcoded value in the Avalonia inspector and feeds the
ImGui panel identically. `IEmbeddedOpenTkNativeHost` exposes it plus a
`StatusChanged` event.

## 3. The command window (the centrepiece, shared behaviour)

Same behaviour in both shells; only the paint differs.

| Behaviour | Rule |
| --- | --- |
| Layout | Output history above, single prompt line below, prompt label bold and non-editable, caret always in the input |
| Prompt line | `ZOOM Specify corner of window, enter a scale factor (nX or nXP), or [All/Center/Extents/Object] <real time>:` — bracket keywords rendered as **clickable links**, abbreviation letters underlined |
| Autocomplete | Dropdown *above* the input (space is below), appears from 1 char, `Tab`/`↓` cycles, `Enter` accepts, `Esc` dismisses only the list on first press |
| Recall | `↑`/`↓` walk input history when the list is closed |
| Repeat | Empty `Enter` repeats last command (already in runtime) |
| Space | Submits unless the active prompt allows arbitrary input (§2.4) |
| Esc | 1st: close list · 2nd: `CANCEL` active command · 3rd: clear text |
| F2 | Toggle expanded history (floating panel, resizable, selectable text, copy) |
| Styling | Echo `Command: X` dim-bold, output default, error red, prompt accent; monospace |
| Sizing | The docked strip is drag-resizable by its top edge, 1–12 lines, persisted with the input history in `commandline.json` |
| Right-click | Recent commands · Recent input · Copy history · Clear history · Expanded history |
| Always focused | Typing anywhere in the window that isn't a viewer shortcut routes the character into the command line, AutoCAD-style |

## 4. Avalonia shell — macOS-native where possible

**Chrome.** `ExtendClientAreaToDecorationsHint` with a unified titlebar; the tool
strip lives in that titlebar row on macOS (NSToolbar look: 28 px symbol-style
buttons, label under icon, no borders), and stays a classic ribbon-ish strip on
Windows. The Row-0 `Menu` collapse on macOS is already correct — keep it, extend
the `NativeMenu`.

**Native menu, correct macOS structure:**
`CloudScope` (About/Preferences…/Quit) · `File` (Open ⌘O, Save Labels ⌘S, Save
Labels to LAS, Load Labels, Clear Labels) · `Edit` (Undo ⌘Z, Confirm ⏎, Cancel ⎋,
Fit, Fit to Ground) · `Select` (Box/Sphere/Cylinder + Label mode/Navigate as
radio items) · `View` (Zoom Extents ⌘0, Standard views ⌃1…⌃6, Projection toggle,
Viewport layouts, Color by ▸, Point size ▸, Attributes, Filter…) · `Window`
(Label Registry ⌘L, Command History ⌘2 / F2) · `Help`.
Windows keeps the same tree in the XAML `Menu` — build **one** menu model in C#
and project it into `NativeMenu` or `Menu` so they cannot drift again.

**Theming.** Drop hardcoded hex; move to a `ThemeVariant`-aware `ResourceDictionary`
and force the dark variant — a point-cloud viewport is read against its surroundings,
so the shell stays dark on every platform. Fonts: `-apple-system`/`SF Pro`
via `FontFamily="{DynamicResource SystemFont}"`, monospace `SF Mono, Cascadia Mono, Consolas`.
Respect *Reduce Motion* / *Increase Contrast* by keeping animation out of the shell.

**Inspector (right, 236 → 280 px, collapsible).** Real data from
`ViewerStatusSnapshot`, grouped like AutoCAD's Properties palette:
*General* (file, point count, visible count) · *View* (projection, camera view,
viewport layout, FPS) · *Display* (color source, point size — a live slider that
issues `POINTSIZE n`, attribute filters) · *Labeling* (active label with colour
swatch, instance id, label mode). Every editable row **round-trips through a
command string** so the command line stays the audit trail.

**Viewport overlays.** Replace the mockups: real axis tripod and a real ViewCube
drawn by the renderer (or removed until the renderer can), the view badge bound
to `Snapshot.ViewName`. Status bar shows only toggles that exist; `SNAP GRID
ORTHO POLAR` goes away until backed by commands.

**Command line.** Implement §3 with a custom `CommandLineControl`
(`ItemsControl` over `CommandLineEntry` for the history — virtualized, auto-scroll
only when already at the bottom, no full-text rebuild) + a `Popup` for
completion + `InlineCollection` hyperlinks for keywords. F2 opens a separate
`CommandHistoryWindow` (on macOS a real utility panel).

**Layout after redesign** (rows): `titlebar+toolbar` · `viewport | inspector` ·
`command window (resizable splitter)` · `status bar`.

## 5. GameWindow shell — everything panel-rendered

**Build.** `EnableImGui` already defaults to true, so the overlay ships by default;
the gate stays as an opt-out for builds without the ImGui package.

**Style pass.** One `ImGuiTheme.Apply()`: dark AutoCAD-ish palette matching the
Avalonia dark variant, `FrameRounding 2`, `WindowRounding 0`, `WindowBorderSize 1`,
DPI-aware font atlas (load a real UI font + a monospace font at
`FramebufferSize/ClientSize` scale — currently the default 13 px bitmap font is
unreadable on Retina).

**Panels** (docked, ImGui docking or manual layout mirroring §4):
1. **Main menu bar** — the same generated menu model as Avalonia, full command
   coverage, checkmarks driven by `ViewerStatusSnapshot`.
2. **Tool strip** — icon buttons under the menu (Open, Navigate, Box, Sphere,
   Cylinder, Fit, Confirm, Cancel, Zoom Extents), active tool highlighted.
3. **Properties panel** (right, collapsible with `Tab`) — same four groups as the
   Avalonia inspector, same command round-trip.
4. **Label registry** — keep the existing window, add colour editing, per-label
   point counts, double-click to activate, and dock it into the right column.
5. **Command window** (bottom) — §3 in full: resizable via `ImGuiWindowFlags`
   drag on the top edge, `ImGuiListClipper` over `CommandLineEntry` with
   per-kind colours, keyword links as `ImGui.SmallButton`s laid out inline on the
   prompt line, completion popup via `ImGuiInputTextFlags.CallbackCompletion` +
   `CallbackHistory` (this is what removes the current manual up/down hack).
6. **F2 history window** — floating, selectable, `Copy all` button.
7. **Status bar** — bottom strip: point counts, FPS/frame time (from
   `FrameTimingDiagnostics`), mode, active label, projection.

**Input routing.** `WantsKeyboard`/`WantsMouse` already exist; formalize the rule:
if ImGui wants the keyboard, only viewer *shortcuts* with modifiers reach
`ViewerController`; plain characters go to the command line.

## 6. Suggested order

1. Core: `CommandLineEntry`, `ActiveOptions`, `CommandCompletionSource`,
   `SubmitPolicy`, `ViewerStatusSnapshot` (§2) — with unit tests, no UI change.
2. Shared menu model + full command coverage in both shells.
3. Avalonia `CommandLineControl` (§3/§4) — the visible win.
4. ImGui theme + un-gate the overlay + command window parity (§5).
5. Inspector/properties on real data, delete the mockup XAML.
6. macOS chrome pass: unified titlebar, native menu tree, theme variants, fonts.


## 7. Implementation status (2026-08-09)

All projects target **net10.0**, and the Avalonia shell is pinned to the dark variant.

Delivered:

* **Core** — `CommandLineEntry` + typed history, `ICommandExecutor.ActiveOptions`
  through runtime/dispatcher/embedded-host chain, `CommandCompletionSource`,
  `CommandLineSession.SpaceSubmits`/`Complete`, `ViewerStatusSnapshot`
  (`ViewerController.Status`, smoothed FPS), and `CommandMenu` — one menu tree
  projected into the macOS `NativeMenu`, the Windows/Linux `Menu`, and the ImGui
  menu bar.
* **GameWindow** — the overlay is now the whole UI: themed via `ImGuiTheme`,
  drawn in physical pixels with system TTF fonts (crisp on Retina), menu bar,
  tool strip, properties panel on live snapshot data, drag-resizable command
  window with per-kind colouring, clickable prompt keywords, Tab completion,
  F2 history window, and a status bar.
* **Avalonia** — theme-variant palette pinned to the dark variant,
  system font stacks, unified titlebar + native menu tree on macOS,
  `CommandLineControl` (typed history, keyword links, completion popup, recall,
  space-vs-enter, Esc peeling), `CommandHistoryWindow` (F2), a real inspector
  bound to `ViewerStatusSnapshot`, real status bar, and a `HISTORY` command.

Deliberately not done: the ViewCube and the axis tripod were removed rather than
reimplemented. They need real geometry in both the OpenGL and Metal overlay renderers plus
visual iteration, which is renderer work, not shell work.

## 8. Consolidation pass (2026-08-09)

Parallel implementations removed:

* `IEmbeddedOpenTkNativeHost` mirrored all nine `ICommandExecutor` members, and each of
  the four layers between the command line and the runtime re-declared them just to
  null-check and forward. They are now a single `Commands` property backed by
  `DelegatingCommandExecutor`, which resolves its target per call and reports "not ready"
  until the viewer exists. `HostController.EmbeddedCommandExecutor` is gone.
* Colours and font stacks lived twice — once in `Themes/Theme.axaml`, once in
  `ImGuiTheme`. Both now come from `CloudScope.Core/Ui/UiPalette`; the Avalonia resource
  dictionary is built from it at startup and `Themes/Theme.axaml` is deleted.
* `HELP` listed commands from a hand-written string that had already drifted. It is now
  generated from the runtime's registered global names, and the console banner points at
  `HELP` instead of repeating a third copy of the list.
* `PointCloudViewer` was an empty subclass whose only content was `IViewerHost`;
  `OpenTkViewerHost` implements the interface directly.
* Dead parallel APIs deleted: `ExecuteViewerCommand`, `HostController.ExecuteCommand`,
  `ViewerCommandDispatcher.ExecuteCommand`/`ExecuteResult`, `CommandCompletionSource.CommonPrefix`.

Performance:

* `ViewerStatusSnapshot` is a `readonly record struct` — the ImGui viewer reads it once
  per frame and no longer allocates on the render path.
* The Avalonia command window appends new history lines (via `CommandLineSession.TotalEntries`)
  instead of rebuilding all 200 rows per command.
* The ImGui command window and history panel submit only on-screen lines through
  `ImGuiListClipper`, instead of all 200 every frame.
* The Avalonia inspector and status bar update only when the snapshot actually changed;
  FPS, the one always-moving value, is written on its own.
* History-line brushes are frozen immutable singletons rather than one brush per row.

Left in place by decision: `MetalNetTriangle` and `MetalTriangleTest` are two unreferenced
Metal triangle spikes — the first on the Metal.NET binding, the second on the SharpMetal
wrappers the application uses. They are kept deliberately as backend experiments and are
not part of the shipping code paths.


## 9. Follow-up fixes (2026-08-09)

* **The 3D view now knows where it is.** `ViewerController.SetViewportInset` reserves the
  framebuffer edges the ImGui panels occupy, and the overlay reports them every frame. The
  centre crosshair, zoom-to-centre, the projection aspect ratio and picking all refer to the
  rectangle the user can actually see instead of the whole window.
* **Historical note — shadowed commands.** This former multi-executor workaround was removed
  on 2026-09-03. Every host now delegates the same `ViewerCommandDispatcher`, so registration
  itself owns the one command namespace and detects collisions before a session begins.
* **Typing `HISTORY` or `LABELS` in the Avalonia shell** opens the same windows the menu
  opens. Previously only the menu items worked and typing them silently toggled a flag in the
  embedded viewer that this shell never renders. The menu now has no special cases at all.
* **Inspector width is persisted** alongside the command-window height and input history;
  `CommandLineSettings` became `ShellSettings` (`shell.json`) to match what it stores.
* **The ImGui command line shows what Tab would complete to** next to the prompt. ImGui's
  input control cannot render an inline selected suffix the way the Avalonia one does, so the
  candidate is displayed rather than pre-inserted.
* Removed an empty `SelectionChanged` handler in the Avalonia completion list.
