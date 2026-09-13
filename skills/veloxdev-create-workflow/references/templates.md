# Scaffolding your views with the item templates

Each adapter ships a `dotnet new` item-template pack that generates the seven view roles with the behaviours already wired, in your namespace and with your colours. This is the normal way to get a working canvas on screen — write the ViewModels yourself (see [model.md](model.md)) and let the templates produce the view layer.

## The seven items

| Kind | Default class name | What it is |
|---|---|---|
| node | `NodeView` | one node's visual |
| slot | `SlotView` | one port's visual |
| link | `LinkView` | one connection's visual |
| tree | `TreeView` | the surface: scroll viewer, canvas, items host, decorators |
| selector | `TemplateSelector` | picks the node / link template |
| decorator | `GridDecorator` | the grid and the ruler |
| minimap | `MinimapOverlay` | thumbnail plus a draggable viewport indicator |

Short names are `{prefix}-v-{kind}` on every pack — the decorator is `-v-decorator`, not `-v-grid`:

| Package | Prefix | Example |
|---|---|---|
| `VeloxDev.WPF.Templates` | `wpf` | `wpf-v-node` |
| `VeloxDev.Avalonia.Templates` | `ava` | `ava-v-tree` |
| `VeloxDev.WinUI.Templates` | `winui` | `winui-v-link` |
| `VeloxDev.MAUI.Templates` | `maui` | `maui-v-slot` |
| `VeloxDev.WinForms.Templates` | `winforms` | `winforms-v-decorator` |
| `VeloxDev.Razor.Templates` | `razor` | `razor-v-minimap` |
| `VeloxDev.Jalium.Templates` | `jalium` | `jalium-v-selector` |

## Generating a suite

```powershell
dotnet new install VeloxDev.WPF.Templates
dotnet add package VeloxDev.WPF

dotnet new wpf-v-tree      -n WorkflowView     -ns MyApp.Views -o Views
dotnet new wpf-v-node      -n NodeView         -ns MyApp.Views -o Views
dotnet new wpf-v-slot      -n SlotView         -ns MyApp.Views -o Views
dotnet new wpf-v-link      -n LinkView         -ns MyApp.Views -o Views
dotnet new wpf-v-selector  -n TemplateSelector -ns MyApp.Views -o Views
dotnet new wpf-v-decorator -n GridDecorator    -ns MyApp.Views -o Views
dotnet new wpf-v-minimap   -n MinimapOverlay   -ns MyApp.Views -o Views

dotnet build
```

⚙ **`-ns` is the namespace and `-n` the class name.** `preferNameDirectory` is `false` on every item, so `-o Views` puts the files directly in `Views/` with no per-name subfolder.

⚙ **`-n` is load-bearing.** The tree view references its siblings by the default class names through a namespace alias, so renaming one means editing the tree by hand. Generate with the defaults unless you are prepared to do that.

⚙ **All seven items must go into one namespace.** That is how the tree resolves its siblings. Splitting them across two namespaces gives you a tree that cannot find its node view.

## Style parameters

Beyond `namespace`, the parameters only affect colours and geometry, and **all seven packs expose the same names with the same CLI aliases and the same defaults** — so a command line moves between GUIs unchanged.

| Item | Options (short → symbol) | Defaults |
|---|---|---|
| node | `-bg` `nodeBackground` · `-fg` `nodeForeground` · `-bb` `nodeBorderBrush` · `-bt` `nodeBorderThickness` · `-cr` `nodeCornerRadius` | `#DDFFFFFF` · `#DD1E1E1E` · `#331E1E1E` · `1` · `6` |
| slot | `-bg` `slotBackground` · `-sc` `slotColor` · `-bc` `slotBorderColor` · `-sp` `slotPath` | `#01000000` · `#DD1E1E1E` · `#FFFFFFFF` · a five-subpath SVG path |
| link | `-lc` `linkColor` · `-lt` `linkThickness` | `#DDFFFFFF` · `2` |
| tree | `-bg` `surfaceBackground` · `-bb` `surfaceBorderBrush` · `-bt` `surfaceBorderThickness` · `-cr` `surfaceCornerRadius` | `#1E1E1E` · `#33FFFFFF` · `1` · `3` |
| selector | — | |
| decorator | `-bg` `gridBackground` · `-mic` `minorGridColor` · `-mac` `majorGridColor` · `-ac` `axisColor` · `-gs` `gridSpacing` · `-mle` `majorLineEvery` · `-rb` `rulerBackground` · `-rtc` `rulerTickColor` · `-rlc` `rulerLabelColor` · `-rdc` `rulerDividerColor` | `#1E1E1E` · `#2A2D2E` · `#3A3D40` · `#4D4D4D` · `40d` · `5` · `#C8252526` · `#555555` · `#888888` · `#3A3D40` |
| minimap | `-bg` `minimapBackground` · `-bdr` `minimapBorder` · `-nf` `nodeFill` · `-vs` `viewportStroke` | `#D2141922` · `#DC94A3B8` · `#DC38BDF8` · `#F0FFFFFF` |

⚙ **A colour you pass may legitimately do nothing on some pack.** Where a framework has no consumer for a value, the parameter is still accepted so the command line stays uniform, and the pack says so in its own `template.json`. The ones to know: **Jalium's minimap colours** and **Razor's tree border and corner radius** are accepted and ignored, and **MAUI has no consumer for `slotPath`** because it draws its ports from geometry rather than from an SVG path. Each `gui/<gui>.md` lists its pack's inert parameters.

⚙ A few declared parameters are inert **without** such a note — `slotBorderColor` on the WPF, WinUI and Avalonia slot templates, `slotColor` on Avalonia's, `slotPath` on MAUI's. If a slot colour refuses to appear, that is the likely reason: edit the generated file directly instead.

⚙ The colour symbols also appear inside comments in the generated files, so a substitution rewrites doc text as well. Cosmetic only.

## What you still have to write

⚙ **The host shell.** These are *item* templates — start from a normal GUI project and add the package. `App`, `Program`, `MainWindow` / `MainPage` / `MainForm`, `MauiProgram` and the app manifest are all yours.

⚙ **The ViewModels.** The tree, node, slot and link ViewModels, your node types and their helpers — see [model.md](model.md). The minimal working set to copy is `Examples/Workflow/<GUI> Trimmed/Demo/ViewModels/Workflow/` in a source checkout, or on GitHub if you installed from NuGet.

⚙ **The wiring.** Add the `VeloxDev.<GUI>` package, set the tree's `DataContext` (or `Tree` parameter) to your `IWorkflowTreeViewModel`, and make sure your node view's `DataContext` is your node ViewModel.

⚙ **Anything the debug HUD does.** The demos carry an `InfoOverlay` read-only diagnostics panel; it is deliberately not part of the packs, because it is a debugging aid rather than part of the editor.

## Checking the generated suite

⚙ **The build is the check** — generation succeeding proves nothing, because `dotnet new` substitutes strings without understanding them. Generate, then `dotnet build`.

⚙ **Avalonia's tree view is the one that does not build as generated.** It ships a placeholder `x:DataType` pointing at a `TreeViewModel` in `MyApp.Views`; point `xmlns:vm` at your ViewModels namespace and rename the type. The error appears at that line, on purpose, rather than as a canvas that silently shows nothing. See [gui/avalonia.md](gui/avalonia.md).

⚙ Everything else should build immediately once the ViewModels exist. If a template's short name is already installed, `dotnet new install` can exit non-zero on the duplicate registration — `dotnet new uninstall VeloxDev.<GUI>.Templates` first.
