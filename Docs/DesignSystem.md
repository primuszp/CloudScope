# CloudScope design system

One visual language for both shells — the Avalonia workspace and the OpenTK/ImGui
viewer. Every colour, metric and font is a token defined once in
`Source/CloudScope.Core/Ui/UiPalette.cs`; the Avalonia resource dictionary and the
ImGui style are both built from it, so the two UIs cannot drift into different-looking
versions of the same product.

## Principles

* **Dark only.** A point-cloud viewport is judged against its surroundings, so the
  shell stays dark on every platform. There is no light variant.
* **Graphite over deep black.** AutoCAD's dark theme: a near-black canvas, graphite
  surfaces stacked in tiers, hairline borders, one azure accent. No gradients, no
  drop shadows in the shell chrome.
* **macOS-native, approximated.** System font stack (`SF Pro Text` first), the menu
  in the system menu bar, a unified titlebar with the tool strip in it, Fluent's
  accent-derived visuals repointed at the CloudScope azure. No AppKit interop —
  Windows gets the same look, only with its own window chrome.
* **The command line is the audit trail.** Every editable control round-trips
  through a command string; the command window wears the same graphite as
  everything else rather than a white console strip.

## Colour tokens

`UiPalette` constant → `Cs*` Avalonia resource → `ImGuiTheme` field. Values are
`0xRRGGBB`.

| Token | Hex | Role |
| --- | --- | --- |
| `ViewportBackdrop` | `0E0F11` | the 3D canvas — deepest black |
| `SurfaceDeep` | `161719` | command well, popups, inset lists |
| `Surface` | `1E2022` | inspector, dialog bodies |
| `SurfaceAlt` | `26282B` | status bar, cards, raised buttons |
| `Graphite` | `303234` | unified titlebar / tool strip band |
| `SurfaceHover` | `34373B` | pointer-over fill |
| `Border` | `3A3D40` | hairline dividers and outlines |
| `BorderStrong` | `4A4E52` | focused / raised edges |
| `Text` | `D8DADE` | primary text |
| `TextDim` | `8A8F96` | labels, captions, section headers |
| `TextFaint` | `5C6167` | disabled text, faint separators |
| `Accent` | `3E8FD0` | prompt, active tool, focus, selection edge |
| `AccentBright` | `5BA6E4` | accent under the pointer |
| `AccentDim` | `2C6B9E` | pressed accent, dim accent fills |
| `SelectionFill` | `24384A` | selected list row / completion candidate |
| `Error` / `Ok` / `Warn` | `E06C6C` / `5FB57A` / `E0A24E` | semantic |

Command-line entry colours derive from these via `UiPalette.EntryColor`:
prompt → `Accent`, error → `Error`, banner/echo → `TextDim`, output → `Text`.

## Metric tokens

| Token | Value | Role |
| --- | --- | --- |
| `RadiusControl` | 5 | buttons, inputs, small controls (`CsCornerControl`) |
| `RadiusCard` | 6 | cards, popups, grouped containers (`CsCornerCard`) |
| `HairlineThickness` | 1 | dividers and outlines |
| `FontSizeBody` / `FontSizeSmall` / `FontSizeMono` | 12 / 10 / 12 | |
| `Space1..Space4` | 4 / 8 / 12 / 16 | the only spacing steps a layout should use |

The ImGui style mirrors these: `FrameRounding` 4, `WindowRounding` 6,
`WindowBorderSize` 1, `WindowPadding` 10×8, `ItemSpacing` 8×6.

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
