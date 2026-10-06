# Jalium

`VeloxDev.Jalium` · targets `net10.0` on `Jalium.UI.Controls` · **platform-neutral** (Windows, Linux and Android)

⚙ **Reference implementation:** `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/` — a `.jalxaml` + `.jalxaml.cs` pair for each of tree, node, slot and link, plus a single `.cs` for `GridDecorator`, `MinimapOverlay` and `TemplateSelector`. `InfoOverlay.*` beside them is the demo's debug HUD, not part of any pack.

⚙ **The "Trimmed" in that path means *minimal demo*, not trim configuration** — the name is a demo-size choice, not a publish setting: `VeloxDev.Core`'s `net8.0` target sets `IsAotCompatible=true` and its agent surface is reflection-free, so the package declares itself usable under trimming and NativeAOT. Do not read publish-time safety into the folder name.

## Writing the surface

As of the 2026-10-05 refactor this platform is **markup-driven with attached behaviors**, like WPF. There is no `WorkflowTreeView` control to derive from: enable `WorkflowSurfaceBehavior` on the tree template's root and name the parts it drives.

```xml
<UserControl xmlns:local="clr-namespace:MyApp.Views"
             xmlns:workflowViews="clr-namespace:MyApp.Views"
             xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.Jalium"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ZoomEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
             behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator"
             behaviors:WorkflowSurfaceBehavior.PointerPressSourceName="PART_SurfaceBorder"
             behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay"
             behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu">
```

| property | meaning |
|---|---|
| `IsEnabled` | master switch; nothing else is read until it is `True` |
| `ScrollViewerName` | the viewer whose offsets are the viewport |
| `CanvasName` | the `Panel` the view pool fills — this is where the pool's `ItemsSource` / `TemplateSelector` attach |
| `GridDecoratorName` | the element answering Core's `IWorkflowGridDecorator`; its `RulerBand` also feeds the virtualize inset |
| `PointerPressSourceName` | the element a *blank* left-press must land on to start a pan |
| `MinimapOverlayName` | the element answering `IWorkflowMinimapOverlay` |
| `ZoomEnabled` | hooks the Ctrl+wheel gesture on the named scroll viewer |
| `LinkMenuKey` | resource key of the link context menu (below) |
| `CanvasTransform` | set **by** the behavior — the world transform the templates bind (below) |

The tree is the host's `DataContext`, and `WorkflowSurfaceBehavior` itself is a `static class` — nothing derives from it. The pool looks its selector up under the fixed resource key `WorkflowTemplateSelector` (`WorkflowSurfaceBehavior.cs:43,683`).

## The canvas transform, and why Jalium spells it differently

`WorkflowSurfaceBehavior.CanvasTransform` publishes the world-to-view `TranslateTransform` **on the host**, and the node and link templates must bind their `RenderTransform` to it — the same carrier WPF gets from `WorkflowCanvasTransformBehavior.Transform`. Jalium cannot bind it by that path: **binding paths here do not resolve prefixed attached properties**, so `(behaviors:WorkflowSurfaceBehavior.CanvasTransform)` finds nothing. The tree view's own class therefore re-exposes the *same* `DependencyProperty` under a plain name:

```csharp
public static readonly DependencyProperty CanvasTransformProperty = WorkflowSurfaceBehavior.CanvasTransformProperty;
public Transform? CanvasTransform => GetValue(CanvasTransformProperty) as Transform;
```

and each template binds that CLR name at its `DataTemplate` root:

```xml
RenderTransform="{Binding CanvasTransform, RelativeSource={RelativeSource AncestorType=UserControl}}"
```

⚙ **This is the one place Jalium must differ from WPF — do not "harmonise" it.** Delete the mirror, or move the binding into `NodeView` where it resolves against the item's own tree, and every card simply stays at the world origin after a pan, with no error.

⚙ The translation sits on the host rather than the canvas so that a slot's anchor can use the other six adapters' contract (below): the translation is inside the measured subtree, so the measurement subtracts it.

## Node cards, ports and the grid

`NodeView.jalxaml` is a `<Viewbox Stretch="Uniform">` over a `Grid` pinned to the **design size** `260 × 180`, as on the other XAML adapters — put your content inside at design coordinates and write nothing for zoom. Its title-bar `Grid` carries `WorkflowNodeDragBehavior` (`IsEnabled` + `CoordinateHostName="PART_Canvas"`; the host type defaults to `Canvas`), which turns a left-drag into `MoveCommand`. Keep it on the bar, not the whole card, or dragging a port will drag the node too.

⚙ **Ports are markup, measured back into the model.** The card names its controls — `local:SlotView x:Name="PART_InputSlot"` and `ItemsControl x:Name="PART_OutputSlots"` — and declares them to `WorkflowSlotLayoutBehavior` as `SlotNames` / `SlotEnumeratorNames`, with `CoordinateHostName="PART_Canvas"`. The behavior finds each control, reads its slot from its `DataContext`, measures its visual centre and writes `slot.Anchor` through Core's `WorkflowSurfaceMath.SlotAnchorFromVisualCenter` (`WorkflowSlotLayoutBehavior.cs:428`) — the same contract the other six adapters use. The old `WorkflowPortGeometry` / `WorkflowPortLayout` are gone.

⚙ Nothing enumerates `node.Slots`, so a slot the card shows but does not name is never measured; rename a `PART_` in the markup and you must rename it in the behavior attribute too. The enumerator branch reads containers through `ItemContainerGenerator` because the slot inside an `ItemsControl` item template is out of the host's name scope.

⚙ **The whole grid renderer lives in the template.** `GridDecorator.cs` is `sealed class GridDecorator : Grid, IWorkflowGridDecorator` — the world-coordinate maths, the ruler tick layout and label formatting, and two self-drawn, hit-test-transparent layers: a bottom one filling the surface and drawing the grid, and a top one (`Panel.ZIndex = 100`) drawing the viewport-fixed ruler bands. The adapter ships no such class. Its palette, `GridStep` and `MajorLineEvery` are assigned in the constructor from the `Template*` symbols — restyle the grid by editing those properties.

## Zoom and pan

⚙ The gesture is `PreviewMouseWheel` on the named scroll viewer, gated on `Keyboard.Modifiers == Control` (`WorkflowSurfaceBehavior.cs:536-560`). **`ZoomEnabled="True"` is the whole switch — the host window writes no zoom code.** Without Ctrl the wheel scrolls normally and is forwarded to Core as pointer input.

⚙ Zoom-in is `Scale *= 1 / 1.1`: `Scale` is a collapse factor, so zooming in *decreases* it. The behavior pins the world point under the viewport centre, commits the new scale and offsets, and re-virtualizes itself (`NotifyZoomCommitted`) — that call is the behavior's own, not something a host calls.

⚙ Panning begins on a left-press reaching the `PointerPressSourceName` element in the preview phase; a press that lands on a card or a port is excluded, so dragging a node moves the node, not the camera.

## Links

`LinkView.jalxaml` is empty markup; the code-behind is yours. It draws a cubic Bézier from `Sender.Anchor` to `Receiver.Anchor`, reads those **straight from the model** (a bound endpoint is a frame stale on a pooled view), and **moves its own layout box onto the curve before drawing**, through `WorkflowLinkBounds.Apply` (`BoxPad = 6`).

⚙ **Keep the self-bounding behaviour.** Jalium's renderer decides whether to draw a child by its *layout box* (`Visual.ShouldRenderChild`), so a link that draws outside its box is not clipped — it is **dropped silently**, and the symptom is links vanishing at deep zoom. The box is a render hint, not a hit target: hit testing goes through the curve the view publishes with `link.PublishCurve(...)` from `OnRender`. Change the shape in both, or the pointer answers for a curve nobody sees; and skip both for a `NaN` endpoint (a placeholder slot's default anchor) or an empty curve.

## The link context menu

⚙ The menu is **a resource plus a key**, not an override. Declare `<ContextMenu x:Key="LinkContextMenu">` in the tree's resources and point `WorkflowSurfaceBehavior.LinkMenuKey` at it; the behavior opens it on a right-press on a link and sets the menu's `DataContext` to that link (`WorkflowSurfaceBehavior.cs:1238,1248`), so each entry is `Command="{Binding DeleteCommand}"` or your own. There is **no `OnBuildLinkMenu` to override and no Delete item added for you** — the template ships the resource empty and the demo adds one entry. Add or remove `<MenuItem>`s there.

## Item templates — `VeloxDev.Jalium.Templates`

**Generates** a `.jalxaml` + `.jalxaml.cs` pair for tree, node, slot and link, and a single `.cs` for selector, decorator and minimap — eleven files. Siblings resolve by default class name through one markup namespace (`xmlns:workflowViews="clr-namespace:<your -ns>"`), as on WPF, so all seven go into one namespace.

| item | short name | you edit |
|---|---|---|
| tree | `jalium-v-tree` | the surface markup: behavior attributes, the node/link `DataTemplate`s, the pool binding, the link menu |
| node | `jalium-v-node` | the card inside the design-size `Viewbox`; keep the `PART_*` names in step with the slot-layout attributes |
| slot | `jalium-v-slot` | the glyph path and the by-state colours in `UpdateForeground` |
| link | `jalium-v-link` | the curve, its `MinimumPull`, the stroke |
| decorator | `jalium-v-decorator` | the whole grid: palette, spacing, major-line count, the drawing |
| minimap | `jalium-v-minimap` | the four palette colours |
| selector | `jalium-v-selector` | which `DataTemplate` each item kind gets |

⚙ **`jalium-v-tree` has no `linkColor` symbol** (only `jalium-v-link` declares it), so `-lc` is rejected on the tree and the tree's `LinkView` entry takes the link item's own default. Change the link colour on `jalium-v-link`.

⚙ **One symbol on this pack is accepted and discarded:** the slot's `slotBorderColor` (`-bc`) declares `replaces: TemplateSlotBorderColor`, but that token appears in no generated file — the slot view paints a filled `Path` tinted from its own `Foreground` and has no border element. Every other declared symbol's token is live. (The tree's `surfaceBorderBrush` / `surfaceBorderThickness` / `surfaceCornerRadius` carry a "this GUI's tree has no border element" note in `template.json`, yet their tokens do sit on the generated `Border` — the note is stale, the substitution happens.)

⚙ Colours are substituted into parser calls (`ColorConverter.ConvertFromString("TemplateSlotColor")`), not into literals — which is why the mirror comparison normalises colour spelling before diffing.

⚙ Verification is one script for all seven platforms: `Src/Verification/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict`. It packs, installs, generates all seven, compiles them together against the adapter, compares each against its `Examples/Workflow/Jalium Trimmed/` mirror (normalising the colour spelling first), and **fails any generated file that still contains a `replaces` placeholder token** — the assertion that catches a template referencing a symbol its pack never declares.
