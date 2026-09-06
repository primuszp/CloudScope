# CloudScope design system

One visual language for both shells — the Avalonia workspace and the OpenTK/ImGui
viewer. Every colour, metric and font is a token defined once in
`Source/CloudScope.Core/Ui/UiPalette.cs`; the Avalonia resource dictionary and the
ImGui style are both built from it, so the two UIs cannot drift into different-looking
versions of the same product.

## Principles

* **Dark only.** A point-cloud viewport is judged against its surroundings, so the
  shell stays dark on every platform. There is no light variant.
* **Graphite over deep black, no accent hue.** AutoCAD's dark chrome carries no
  accent colour and neither does this — a near-black canvas, neutral graphite
  surfaces stacked in tiers, hairline borders. Emphasis (prompt text, focus, the
  active tool) is a bright neutral grey; only `Ok` / `Warn` / `Error` are coloured.
  The neutrals track the Autodesk brand's charcoal ramp. No gradients, no drop
  shadows in the shell chrome.
* **macOS-native, approximated.** Font stack led by Autodesk's `Artifakt Element`
  then `SF Pro Text`, the menu in the system menu bar, a unified titlebar with the
  tool strip in it, Fluent's accent-derived visuals repointed at the neutral
  emphasis greys. No AppKit interop — Windows gets the same look, only with its own
  window chrome.
* **Regions are separated by tone, not lines.** The black viewport, the graphite
  tool band, the sunken command well and the raised status strip each read as their
  own plane; the shell draws no dividers between them. The only framed element is
  the view badge (a card) and floating popups. Splitters are invisible grab strips.
* **One radius, one hairline, a 4px grid.** Every corner is `RadiusControl` (3) or
  `RadiusCard` (5); every drawn edge is a 1px hairline; every margin, padding and
  gap is a multiple of 4.
* **The command line is the audit trail.** Every editable control round-trips
  through a command string; the command window is one dark well — transcript,
  prompt line, and two quiet affordances in the corner — not a bordered console.

## Colour tokens

`UiPalette` constant → `Cs*` Avalonia resource → `ImGuiTheme` field. Values are
`0xRRGGBB`.

| Token | Hex | Role |
| --- | --- | --- |
| `ViewportBackdrop` | `0F0F0F` | the 3D canvas — deepest black |
| `SurfaceDeep` | `171717` | command well, popups, inset lists |
| `Surface` | `1F1F1F` | inspector, dialog bodies |
| `SurfaceAlt` | `272727` | status bar, cards, raised buttons |
| `Graphite` | `323232` | unified titlebar / tool strip band (≈ Autodesk charcoal-900) |
| `SurfaceHover` | `373737` | pointer-over fill |
| `Border` | `3D3D3D` | hairline dividers and outlines |
| `BorderStrong` | `4E4E4E` | focused / raised edges |
| `Text` | `DADADA` | primary text |
| `TextDim` | `909090` | labels, captions, section headers (≈ Autodesk charcoal-700) |
| `TextFaint` | `616161` | disabled text, faint separators |
| `Accent` | `C2C2C2` | emphasis, not a hue: prompt, active tool glyph, focus outline |
| `AccentBright` | `DEDEDE` | emphasis under the pointer; prompt lines in the transcript |
| `AccentDim` | `3E3E3E` | lifted-graphite fill for a pressed / active control, status strip |
| `SelectionFill` | `343434` | selected list row / completion candidate |
| `Error` / `Ok` / `Warn` | `E06C6C` / `5FB57A` / `E0A24E` | semantic — the only colours in the UI |

Command-line entry colours derive from these via `UiPalette.EntryColor`:
prompt → `AccentBright`, error → `Error`, banner/echo → `TextDim`, output → `Text`.

## Metric tokens

| Token | Value | Role |
| --- | --- | --- |
| `RadiusControl` | 3 | buttons, inputs, small controls (`CsCornerControl`) |
| `RadiusCard` | 5 | cards, popups, grouped containers (`CsCornerCard`) |
| `HairlineThickness` | 1 | every drawn edge in the shell |
| `FontSizeBody` / `FontSizeSmall` / `FontSizeMono` | 12 / 11 / 12 | |
| `Space1..Space4` | 4 / 8 / 12 / 16 | the only spacing steps a layout should use |

The ImGui style mirrors these: `FrameRounding` 3, `WindowRounding` 5,
`WindowBorderSize` 1, `WindowPadding` 12×8, `ItemSpacing` 8×6.

## Fonts

`UiFontStack` = `SF Pro Text, Helvetica Neue, Segoe UI Variable Text, Segoe UI,
Inter, sans-serif`. `MonoFontStack` = `SF Mono, Menlo, Cascadia Mono, Consolas,
DejaVu Sans Mono, monospace`. First family present on the platform wins. The ImGui
viewer loads the nearest real TTF at the display's scale so text stays crisp on
Retina.

## Adding a themed control (Avalonia)

1. Never write a hex value in a control or window. Bind to a `Cs*`
   `DynamicResource`, or, in code, build the brush from a `UiPalette` constant (see
   the `Frozen` / `Brush` helpers in `CommandLineControl` and `LabelRegistryWindow`).
2. Structural styling — shape, state, hover/pressed/selected — belongs in
   `Source/CloudScope.Avalonia/Themes/Controls.axaml`, as a `Style` selector layered
   over Fluent. That file names no colours of its own.
3. `App.cs` registers every token as a resource, repoints the `SystemAccentColor`
   family, and forces the `TextBox` onto `SurfaceDeep` in every visual state. A new
   token added to `UiPalette.NamedColors` / `NamedMetrics` reaches the shell
   automatically.
4. A framed group of content is `Classes="card"`; a toolbar button is
   `Classes="tool"`.

## Adding to the ImGui viewer

Pull the colour from an `ImGuiTheme` field (add one, sourced from a `UiPalette`
token, if it is missing). Push it with `ImGui.PushStyleColor` and pop it in the
same scope. Panels that own a band of chrome (`title`, `menu`, tool strip, status
bar) use `ImGuiTheme.Graphite`; sunken content uses `SurfaceDeep`.
