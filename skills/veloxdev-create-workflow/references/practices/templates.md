# Scaffolding your views with the item templates

Each adapter ships a `dotnet new` item-template pack that generates the seven view roles with the behaviours already wired, in your namespace and with your colours. This is the normal way to get a working canvas on screen — write the ViewModels yourself (see [model.md](model.md)) and let the templates produce the view layer.

⚙ **Generate all seven roles.** The decorator supplies the floating top/left ruler bands; the minimap supplies the thumbnail and navigation. A tree + node + link suite has neither. The **Generating a suite** block below is the default — run every command in it, then add your content to the generated node view.

⚙ **Install the pack that matches your adapter package.** A pack is built against the adapter it ships with; install the latest published version of both.

⚙ **The generated node view's port set is the template's default surface, not the node model's contract.** On the attached-property packs, the item template draws one input and one output slot and registers those two controls with the layout behaviour; Razor supplies the same two through render fragments. A node type whose ViewModel exposes other slots keeps the same name-based correspondence for each slot it wants measured — see [view-layer.md](view-layer.md#ports-the-model--view-correspondence).

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

⚙ **Generate the whole suite.** The tree names its selector, decorator and minimap siblings through the namespace alias. Leaving one out means editing the tree by hand, and leaving out the decorator or minimap ships a canvas with no floating rulers or thumbnail. `preferNameDirectory` is `false` on every item, so `-o Views` puts the files directly in `Views/` with no per-name subfolder.

⚙ **`-n` is load-bearing.** The tree view references its siblings by the default class names through a namespace alias, so renaming one means editing the tree by hand. Generate with the defaults unless you are prepared to do that.

⚙ **All seven items go into one namespace.** That is how the tree resolves its siblings. Splitting them across two namespaces gives you a tree that cannot find its node view.

## The style contract

Beyond `namespace`, the parameters only affect colours and geometry, and **all seven packs expose the same names with the same CLI aliases** — so a command line moves between GUIs unchanged. Each item's aliases live in `.template.config/dotnetcli.host.json` (`symbolInfo.shortName`); `template.json` declares the symbol and its default. Both files are required to reproduce the CLI surface — an alias in `template.json` alone does nothing.

| Item | Option (short → symbol) | Meaning |
|---|---|---|
| node | `-bg` `nodeBackground` | card fill |
| | `-fg` `nodeForeground` | card text and glyph colour |
| | `-bb` `nodeBorderBrush` | card border |
| | `-bt` `nodeBorderThickness` | card border width |
| | `-cr` `nodeCornerRadius` | card corner radius |
| slot | `-bg` `slotBackground` | slot hit-test / backing fill |
| | `-sc` `slotColor` | slot standby colour |
| | `-bc` `slotBorderColor` | slot border colour (where the slot draws a border) |
| | `-sp` `slotPath` | the SVG path the slot glyph is drawn from |
| link | `-lc` `linkColor` | settled link stroke |
| | `-lt` `linkThickness` | settled link stroke width |
| tree | `-bg` `surfaceBackground` | surface fill |
| | `-bb` `surfaceBorderBrush` | surface border |
| | `-bt` `surfaceBorderThickness` | surface border width |
| | `-cr` `surfaceCornerRadius` | surface corner radius |
| decorator | `-bg` `gridBackground` | grid background |
| | `-mic` `minorGridColor` | minor grid line |
| | `-mac` `majorGridColor` | major grid line |
| | `-ac` `axisColor` | axis line |
| | `-gs` `gridSpacing` | spacing between grid lines |
| | `-mle` `majorLineEvery` | how many minor lines make a major line |
| | `-rb` `rulerBackground` | ruler band fill |
| | `-rtc` `rulerTickColor` | ruler tick |
| | `-rlc` `rulerLabelColor` | ruler label |
| | `-rdc` `rulerDividerColor` | ruler divider |
| minimap | `-bg` `minimapBackground` | thumbnail background |
| | `-bdr` `minimapBorder` | thumbnail border |
| | `-nf` `nodeFill` | node mark fill |
| | `-vs` `viewportStroke` | viewport indicator stroke |

⚙ **A colour symbol is a contract even where the framework has no consumer.** Where a framework draws no border or no SVG path, the pack still declares the symbol for CLI parity and says so in its own `template.json`. The command line stays uniform; the generated file documents the inert value. When a colour refuses to appear, edit the generated file directly rather than the command line.

⚙ **The colour symbols also appear inside comments in the generated files**, so a substitution rewrites doc text as well. That is cosmetic.

## The template's defaults

The generated files are defaults, not fixtures. The node view's `InputSlot` / `OutputSlots` pair is the template's two default implementations; the link menu resource is empty; the selector maps the two template kinds. Edit the generated files for your node types, your menu entries and your template selection.

⚙ **The host shell is yours.** These are *item* templates — start from a normal GUI project and add the package. `App`, `Program`, `MainWindow` / `MainPage` / `MainForm`, `MauiProgram` and the app manifest are supplied by your GUI project.

⚙ **The ViewModels are yours.** The tree, node, slot and link ViewModels, your node types and their helpers — see [model.md](model.md). The minimal working set to copy is `Examples/Workflow/<GUI> Trimmed/Demo/ViewModels/Workflow/` in a source checkout, or on GitHub if you installed from NuGet.

⚙ **The wiring is yours.** Add the `VeloxDev.<GUI>` package, set the tree's `DataContext` (or `Tree` parameter) to your `IWorkflowTreeViewModel`, and make sure your node view's `DataContext` is your node ViewModel.

⚙ **The link menu is declared and empty.** The tree item declares the menu and wires the right-press to it — `LinkMenuKey` plus a resource on the markup platforms (WPF/Avalonia/WinUI/MAUI/Jalium), a `<LinkMenu>` fragment on Razor, an `OnBuildLinkMenu` override on WinForms — and every entry is yours. Add the entries there rather than writing right-press code; each entry is bound to the pressed link, so `Command="{Binding DeleteCommand}"` (or the GUI's equivalent) is the whole entry. See [view-layer.md](view-layer.md#the-link-context-menu).

⚙ **The debug HUD is not part of the packs.** The demos carry an `InfoOverlay` read-only diagnostics panel; it is a debugging aid supplied by the host.

## Checking the generated suite

⚙ **The build is the check** — generation succeeding proves nothing, because `dotnet new` substitutes strings without understanding them. Generate, then `dotnet build`.

⚙ **A pack may ship a placeholder `x:DataType` that fails the build until you repoint it.** Avalonia's tree view is the documented case: it points at a `TreeViewModel` in `MyApp.Views`, and that compile failure is intentional — a clear error beats a canvas that silently renders nothing. Point `xmlns:vm` at your ViewModels namespace and rename the type. See [gui/avalonia.md](gui/avalonia.md).

⚙ **Reinstall the pack when its short name is already registered.** `dotnet new install` can exit non-zero on a duplicate registration — `dotnet new uninstall VeloxDev.<GUI>.Templates` first.

When a generated suite does not build or its tree cannot find a sibling, read [references/pitfalls/templates.md](../pitfalls/templates.md).
