# 叠影 · Dieying

[GitHub repository](https://github.com/mayday-stacy/Dieying) · [中文说明](README.zh-CN.md) · [User guide](docs/README.md) · [Changelog](CHANGELOG.md) · [Building and releasing](RELEASING.md)

Dieying is an open-source, layer-based image editor for everyday retouching and compositing, with Photoshop-style tools and shortcuts. Development focuses on Windows, Chinese text input and an English/Simplified Chinese interface.

This is an independent MIT-licensed fork of [Composa](https://github.com/dvdstelt/Composa), starting at its `v1.4.0` tag (`86529d3b5355d28ddc28ec3db2e4de1d4d00f63b`). Composa, by Dennis van der Stelt, reimplements Robbie Tilton's macOS [Compositor](https://github.com/robbietilton/Compositor) in C#/.NET, Avalonia and SkiaSharp. Dieying builds on that work; it is not an official release of either upstream project.

## Current status

Dieying is a development edition. The supported packaging path produces a self-contained Windows portable folder and ZIP; Windows x64 is the primary validation target. `win-arm64` can be built but needs device testing. The source remains cross-platform and Linux CI checks the editor, but independent Linux packages and macOS distribution have not been prepared.

The Windows package includes .NET, ImageMagick and the local image models. Extract the whole ZIP and run **`dieying.exe`**. It does not install an application, register file associations or update another editor. Builds are currently unsigned. See [Windows security and signing](README.zh-CN.md#smart-app-control-与正式签名) for the distinction between a checksum and a trusted signature.

Do not use Composa's downloads or installers to install Dieying. The inherited Composa packaging and public release workflows are disabled; a source repository or CI artifact is not a signed product release. Dieying's `local` update channel does not check for, download or install Composa releases.

## Build, test and run on Windows

Install Git and the .NET 10 SDK selected by [global.json](global.json). Clone the independent repository with its history and tags, then run the PowerShell workflow:

```powershell
git clone https://github.com/mayday-stacy/Dieying.git
Set-Location Dieying
.\scripts\windows.ps1 -Action Run
.\scripts\windows.ps1 -Action Build
.\scripts\windows.ps1 -Action Test
.\scripts\windows.ps1 -Action Publish -Runtime win-x64 -Zip
```

The first run restores NuGet dependencies and downloads the pinned MODNet model (about 26 MB). All three models are checked by size and SHA-256 before use. The application never downloads models at runtime. `-Action Models` prepares them without building; `-SkipModels` is available only for development actions, not packaging.

`Publish` creates a fresh folder under `dist/`, a ZIP when `-Zip` is passed, and a matching `.zip.sha256` file. It does not upload anything. Compare a downloaded ZIP with its published checksum using `Get-FileHash -Algorithm SHA256 <path-to-zip>`; a matching hash checks the file's bytes, not the identity of its publisher. Keep the licence files next to the program.

All script actions and ordinary source builds use the `local` update channel. `Run` isolates development settings and recovery under `artifacts/local-data`; direct launches use Dieying's own profile. See [settings and paths](docs/settings-and-updates.md).

The version is derived by MinVer from Git history and tags. Clone with history and tags intact; do not hand-edit version numbers. Inherited Composa tags describe the fork's starting history, not a prior Dieying release.

For source development on another platform, prepare models with `scripts/models/fetch.sh` and run `dotnet run --project src/Composa.App`. Source paths and C# namespaces intentionally retain `Composa` to keep the fork easy to compare with upstream.

## What this fork adds

- Independent `dieying` executable, application identity, settings, recovery and MCP connection, preserving `.cmps` projects and upstream credits.
- A native PowerShell workflow with verified models, portable packaging and test reports that fail if no tests actually ran.
- Simplified Chinese/English UI, canvas IME preedit and candidate positioning, font fallback, missing-font notices, grapheme-aware editing and common Chinese punctuation line-break rules.
- Shared PNG/JPEG/WebP export controls for output dimensions, encoded preview and file size, independent quality preferences and JPEG matte; background encoding and safe file replacement without resizing or marking the project saved.
- Regression coverage for Chinese composition, IME cancellation and resource lifetime, Unicode editing, export, identity separation and recovery.

The layer editor, filters, file readers and local vision models below are inherited from Composa and maintained in this fork. The detailed list is retained to describe the available editing foundation, not to claim each feature was written for Dieying.

## Features

### Layers
- Layers and folders with 24 blend modes, grouped in the menu as Photoshop groups them, and opacity
- Layer effects: Stroke (outside or inside), Drop Shadow, Outer Glow, Inner Glow, Color Overlay and Inner Shadow, each switchable, editable with a live preview and copied between layers by Alt-dragging
- Layer masks on layers, folders and adjustment layers: paint, fill, gradient, invert, blur, apply, disable
- Clipping masks (Alt-click a layer, or Ctrl+Alt+G)
- Adjustment layers: Hue/Saturation, Levels, Curves, Exposure, Gradient Map, Color Lookup, Grain, Brightness/Contrast, Black & White, Color Balance, Invert, and the live Gaussian Blur, Motion Blur and Add Noise, which work on everything beneath them
- Merge Down, Merge Layers, Merge Group (Ctrl+E) and Flatten Image
- Duplicate (several at once, stacked together above the topmost), rename inline, reorder and nest by drag and drop; Alt-drag to duplicate
- Copy and paste whole layers, folders and adjustments included, within a project or into another tab, where they arrive centered; a right-click menu on every row for the layer, its folder and its mask
- Swipe down the eye column to show or hide many layers; Alt-click an eye to solo a layer

### Transform
- Non-destructive move, scale, rotate and flip: images keep their full resolution however small you make them
- Free distort by Ctrl-dragging a corner; the handles follow the corners, which keep distorting once the layer is distorted, and a corner dragged past the opposite edge folds the layer over itself
- Ctrl-drag moves the current layer with any tool active, as Photoshop's temporary Move tool does
- Auto Select picks the layer under the pointer, including one stacked on a selected background that covers the canvas; turn it off to drag the current layer from anywhere (Ctrl-click still picks)
- Transform several layers, or a whole folder, together
- Snapping to canvas and layer edges and centers, with guides
- Exact values for position, size and angle; arrow keys nudge (Shift for 10 px)
- Live shape layers (rectangle, rounded rectangle, ellipse, line) that are redrawn sharp when scaled
- Rulers, guides dragged out of them, a layout grid with its own color, style, spacing and subdivisions (View > Grid Settings), and snapping of moves, marquees, shapes, selection outlines and crops to guides, grid, layers and the canvas (View > Snap To)

### Selections
- Rectangle and Ellipse Marquee, Freehand and Polygonal Lasso, Magic tool with Wand (similar colors) and Object (the thing under the click) modes
- Add, subtract and intersect; move the outline; move or duplicate the pixels inside
- Select All, Inverse, Subject, Color Range (click colors on the canvas, with fuzziness and invert), Expand, Contract, Feather (also as buttons with amounts in the tool bar); load a layer's pixels or mask as a selection
- Content-Aware Fill, which can also extend an image past its edges

### Painting and retouching
- Brush and Eraser with size, hardness, stroke-level opacity and Smoothing, which trails the pointer so a shaky hand still draws a smooth line; Shift-click for straight lines
- Spot Healing Brush (content-aware)
- Clone Stamp, aligned or not, sampling one layer or all of them
- Smear tool: Liquify (push), Blur, Smudge, Dodge and Burn
- Gradient tool (linear or radial, to background or to transparent) that stays adjustable: drag either end, Enter applies
- Type tool: type straight onto the canvas as point text or in a dragged-out paragraph box, with font, size, style, color, alignment, tracking and leading in the tool bar, the font, style and color per letter; text stays editable and sharp when scaled
- Eyedropper and a full color picker
- Pen pressure varies the brush size on graphics tablets
- Every painting tool also works on masks

### Adjustments and filters
- Levels (with Auto and a histogram), Curves, Hue/Saturation (master and six color ranges, Colorize), Exposure, Gradient Map, Grain, Brightness/Contrast, Invert; Color Lookup through `.cube` and `.3dl` tables, with four bundled film looks drawn on your picture, and Export Look to write your own adjustments as a `.cube`
- Black & White with Photoshop's six color weights, so reds and greens stay apart instead of flattening into one gray, and an optional tint for sepia or cyanotype; Color Balance for shadows, midtones and highlights separately, with Preserve Luminosity
- Gaussian Blur and Motion Blur that spread past a layer's edges, Sharpen, Add Noise (uniform or Gaussian), Lens Correction
- Finishing filters: Vignette in any color (on an empty layer it paints across the whole canvas), Bloom / Glow, Tonal Contrast and Dither (Atkinson, Floyd-Steinberg, Bayer, halftone, Mac patterns and ASCII, in two colors or the picture's own)
- Camera Raw Filter: a grade panel with Light, Color (Auto white balance and an eyedropper), Effects (texture, clarity, dehaze, glow, vignette, grain), Curve, Color Mixer, Color Grading, Detail, Optics and Calibration, each group switchable off without losing its sliders, with a histogram of the result
- Live previews, limited to the selection when there is one

### Canvas and files
- Multiple projects in tabs
- Crop with a ratio picker (Original, 1:1, 4:3, 3:4, 16:9, 9:16), snapping, Shift to keep proportions, Alt for symmetric cropping; with a selection the crop box starts at its bounds
- Trim to transparent pixels or to a corner's color, on the edges you choose
- Zoom In and Zoom Out step through fixed stops, so ten steps in and ten out land back where they started
- Canvas Size, Image Size, and quarter-turn rotation of the canvas or of single layers
- Smooth downsampling when zoomed out, crisp pixels and a pixel grid when zoomed in
- Open PNG, JPEG, WebP, BMP and GIF (and HEIC, AVIF, TIFF and SVG through ImageMagick, included in the Windows portable build); drop files onto the window; paste images from other apps. An SVG placed into a document is drawn to fit the canvas, so a small icon comes in sharp
- Open camera RAW files (Canon, Nikon, Sony, Fujifilm, DNG and more) through ImageMagick: a develop step with exposure, temperature and tint and a live preview comes first, working on a 16-bit decode, so you choose what to keep before the image becomes an 8-bit layer
- Open GIMP files, `.xcf` from 2.10 through 3.2: layers, folders, masks, opacity, blend modes, guides and every precision come in, with the same report of conversions before anything is applied
- Open Photoshop files, `.psd` and Large Document `.psb`: layers, folders, masks, clipping, opacity, blend modes, solid fill shapes, adjustments and simple horizontal text come in editable, and a report lists everything that has to be converted before anything is applied; dropped onto an open document, a Photoshop file arrives inside a folder
- Export PNG, JPEG and WebP with output dimensions, aspect-ratio lock, an encoded preview and file size; separate JPEG/WebP quality, JPEG background color and Copy Merged
- Undo and redo with a History panel; older steps are trimmed by the history count and memory budgets
- Tools that come in groups open beside their toolbar button when it is held or right-clicked, as in Photoshop: the marquees, the lassos, Magic Wand and Object Selection, Brush and Eraser, the Smear modes and the shapes
- Tool settings stick between launches: Auto Select, the transform controls, the pixel grid, rulers, guides, the grid and its settings, Snap and the Snap To options keep what you last set them to
- Autosave for crash recovery: unsaved work is copied to Dieying's own recovery folder every two minutes and offered back after an unclean exit; see [settings and paths](docs/settings-and-updates.md)

A user guide covering every tool, menu and the AI control is in [docs/](docs/README.md).

## AI control

Help > Allow AI Control enables a local MCP server; it is off by default. Point your MCP client at the full path to `dieying.exe` with the argument `--mcp`. Add `--launch` to start the editor when connecting if it is not running. See [AI control](docs/ai-control.md) and the [tool reference](docs/ai-tools-reference.md).

The Windows pipe is `dieying-mcp`; `DIEYING_MCP_PIPE` overrides it. Existing `composa://` resource URIs are retained for protocol compatibility and do not connect to Composa. An external AI client may send the document content it reads to its own provider; choose that client accordingly.

## Compatibility and unfinished work

- Native projects use `.cmps`, compatible with the inherited project format. Compositor's macOS `.comp` packages cannot be opened.
- Photoshop `.psd`/`.psb` import is limited to 8-bit RGB. Unsupported effects, smart objects and some text/adjustments are converted or omitted, with a report before import. PSD export, CMYK and a full 16-bit editing pipeline are not implemented.
- GIMP XCF imports are converted to the editor's 8-bit sRGB representation; unsupported features are reported. ImageMagick handles camera RAW through a develop step, but editing afterwards is 8-bit.
- Layer masks stay linked to their layers. Color management and advanced Photoshop feature parity are incomplete.
- Font fallback and grapheme-aware editing do not provide a complete complex-script shaping or color-emoji pipeline. Windows IME reconversion of committed text is not implemented; additional IMEs and mixed-DPI displays still need device testing.
- OCR/image-text translation, recorded actions and a complete batch-processing interface remain future work. MCP automation does not replace these user-facing workflows.
- Large real-world projects, difficult hair/transparency cutouts, graphics tablets and Windows ARM64 need broader practical validation.
- A Dieying installer, independent file associations and trusted signing are not available yet. These are separate from publishing the open-source repository.

For details, see the [file guide](docs/files.md), [text guide](docs/text.md) and [Chinese development notes](README.zh-CN.md).

## Tests and contributions

Use `scripts/windows.ps1 -Action Test` on Windows. It runs both test projects, retains separate console logs and TRX reports under a unique `artifacts/windows-tests/` folder, and requires actual passing test execution. Run `scripts/tests/windows-test-reports.ps1` when changing that runner. On other supported development platforms, use `dotnet test` after preparing dependencies and models.

- `tests/Composa.Core.Tests` drives `EditorSession`: compositing, tools, filters, imports, projects, undo and randomized edit regressions.
- `tests/Composa.App.Tests` exercises Avalonia headless windows with real Skia rendering. Screenshots go to `artifacts/screenshots/`; Chinese workflow tests also generate editable examples in `artifacts/acceptance/`.

Headless tests do not replace real IME, graphics tablet, multi-monitor or packaging validation. Generated examples, logs, build outputs and local preferences are not source files and should not be committed. Report reproducible bugs in [this repository's issues](https://github.com/mayday-stacy/Dieying/issues). Before contributing, read [AGENTS.md](AGENTS.md) for the document/history invariants, and use a small change with relevant tests and a clear explanation of its behavior.

## Shortcuts

| Keys | Action |
| --- | --- |
| V M L W C | Move, Marquee, Lasso, Magic, Crop (M and L again switch variants) |
| B E J S R | Brush, Eraser, Spot Healing, Clone Stamp, Smear |
| G U T I H Z | Gradient, Shape, Type, Eyedropper, Hand, Zoom |
| Tab | Switch the current tool's mode (Wand/Object, Brush/Eraser, the shape, and so on) |
| Ctrl+drag | Move the current layer with any tool |
| Space, middle button, Ctrl+wheel | Pan, zoom at the cursor |
| Ctrl+0, Ctrl+1 | Fit canvas, actual pixels |
| [ ] and { } | Brush size and hardness |
| 1 to 0 | Brush opacity, or layer opacity with the Move tool |
| X, D | Swap colors, reset to black and white |
| Alt+Backspace, Ctrl+Backspace | Fill with foreground, background |
| Shift+Backspace | Content-Aware Fill |
| Ctrl+A, Ctrl+D, Ctrl+Shift+I | Select all, deselect, inverse |
| Ctrl+J | Duplicate layer, or layer via copy with a selection |
| Ctrl+G, Ctrl+E, Ctrl+Alt+G | Group, merge, clipping mask |
| Ctrl+L, Ctrl+M, Ctrl+U, Ctrl+I | Levels, Curves, Hue/Saturation, Invert |
| \ | Switch between painting the layer and its mask |
| Ctrl+R, Ctrl+', Ctrl+; | Rulers, grid, guides |
| Ctrl+Shift+;, Ctrl+Alt+; | Snap, lock guides |
| Ctrl+Alt+A | Select Subject |
| While typing: Ctrl+Enter, Escape, Alt+arrows | Finish, cancel, tracking and leading |

Every shortcut can be changed in Help > Keyboard Shortcuts (F1).

## Licence and provenance

Dieying's application source is distributed under the [MIT licence](LICENSE), retaining the copyright notices for Dennis van der Stelt and Wonder Assembly LLC. No upstream author endorsement is implied.

Redistributed libraries, model weights and film looks have their own notices in [THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt); the Windows package also carries ImageMagick's notice. Files adapted from Lolly retain their source and permission headers. Keep these notices with any redistributed build.
