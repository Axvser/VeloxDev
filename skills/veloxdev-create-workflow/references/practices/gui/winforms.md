# Windows Forms

`VeloxDev.WinForms` · targets `net10.0-windows` · version 11.2.0

There is a long-form adapter README at `Src/Adapters/VeloxDev.WinForms/README.md`. This is the summary a consumer needs.

⚙ **Reference implementation:** `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/` — the four markup items are the tree, node, slot and link views (`*.xaml` + `*.xaml.cs`); `GridDecorator`, `MinimapOverlay` and `TemplateSelector` are code-only. The Full demo lives at `Examples/Workflow/WinForms/Demo/Views/Workflow/`.

⚙ **The "Trimmed" in that path means *minimal demo*.** It is a demo-size choice, not a publish setting — see [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

## The adapter is XAML-first

WinForms has no platform XAML, so this adapter ships its own small runtime (`Src/Adapters/VeloxDev.WinForms/Xaml/`) and a Roslyn generator (`Src/Generators/VeloxDev.WinForms.Generator/`). The generated `InitializeComponent()` builds ordinary WinForms controls:

- `v:` prefix = `VeloxDev.WinForms.Xaml` / `.Controls` (`UserControl`, `Panel`, `Border`, `ScrollViewer`, `ScrollBar`, `Style`, `Trigger`, `DataTemplate`, …).
- Native WinForms types are written without a prefix (`Label`, `TableLayoutPanel`, `Button`, …).
- `local:` is your own namespace; the item templates use it for sibling views.
- Dependency properties, attached properties, `{Binding}`, `{StaticResource}`, `Style`, `Trigger`, `DataTrigger`, `MultiTrigger`, `TransitionAction`, `DataTemplate` and `ControlTemplate` are supported.
- An **object resource** is an empty element with an `x:Key`: `<local:TemplateSelector x:Key="WorkflowTemplateSelector" />`.
- Every DP change on a XAML `Control` calls `Invalidate()` automatically. Mark configuration properties with `new PropertyMetadata(...) { AffectsRender = false }` if they never paint.

```xml
xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml;assembly=VeloxDev.WinForms"
xmlns:local="clr-namespace:MyApp.Views"
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WinForms"
```

⚙ **Add the generator to the consuming project**, not only the adapter package:

```xml
<ItemGroup>
  <ProjectReference Include="…/VeloxDev.WinForms.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <AdditionalFiles Include="**/*.xaml" />
</ItemGroup>
```

The XAML views take `Control`/`UserControl`/`Panel` from the XAML runtime, so demos remove the implicit WinForms using:

```xml
<Using Remove="System.Windows.Forms" />
```

## Writing the surface

The tree template is a `v:UserControl`; the parts are resolved by native `Control.Name`, and the attached properties are declared on the root:

```xml
<v:UserControl x:Class="MyApp.Views.TreeView"
               behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
               behaviors:WorkflowSurfaceBehavior.ZoomEnabled="True"
               behaviors:WorkflowSurfaceBehavior.TemplateSelector="{StaticResource WorkflowTemplateSelector}"
               behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
               behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
               behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator"
               behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay"
               behaviors:WorkflowSurfaceBehavior.PointerPressSourceName="PART_Surface">
  <v:UserControl.Resources>
    <v:ResourceDictionary>
      <local:TemplateSelector x:Key="WorkflowTemplateSelector" />
    </v:ResourceDictionary>
  </v:UserControl.Resources>
  <v:Panel Name="PART_Surface" Dock="Fill">
    <local:GridDecorator Name="PART_GridDecorator" Dock="Fill">
      <v:ScrollViewer Name="PART_ScrollViewer" Dock="Fill">
        <local:SurfaceCanvas Name="PART_Canvas" Dock="Fill" />
      </v:ScrollViewer>
    </local:GridDecorator>
  </v:Panel>
</v:UserControl>
```

⚙ **`x:Name` and `Name` are different things.** `x:Name` creates a code-behind field; the behavior resolves native `Control.Name`, so the `PART_*` contract lives in `Name`. The tree template writes both when the field is useful.

⚙ **The tree arrives through `SetWorkflowTree`** — typically set from your code-behind (`ViewModel` setter). `Refresh(host)` is part of the contract: a node that moved without a refresh is still drawn where it was.

⚙ **A synchronous escape hatch exists.** `WorkflowSlotLayoutBehavior.SyncNow(control)` re-measures a node's slots in the same frame; node drag and zoom use it. A deferred refresh is a frame stale and the link visibly detaches from its port.

## Links — one layer paints the whole graph

⚙ **Links are not pooled.** `ViewLinkLayer.IsActive=true`, and `WorkflowLinkLayer` draws every link in the tree's **visible set** inside `PART_Canvas`'s `Paint` pass. One canvas paint replaces one child window per link; the Full benchmark drops from 13.9 s to 4.14 s (2000 nodes / 3000 links).

```xml
<local:SurfaceCanvas Name="PART_Canvas" Dock="Fill"
                     behaviors:WorkflowLinkLayer.IsEnabled="True"
                     behaviors:WorkflowLinkLayer.LineColor="#DDFFFFFF"
                     behaviors:WorkflowLinkLayer.Thickness="2" />
```

The layer publishes each curve to Core, so hit-testing, `Delete` and the link context menu use the same curve it drew. It also exposes `HoveredLineColor` / `HoveredThickness`, `VirtualLineColor` / `VirtualValidTargetColor` / `VirtualInvalidTargetColor`, `Glow`, and `FlowHead` / `FlowIntensity` for the travelling highlight.

⚙ **Use the node-local anchor for endpoints.** `WorkflowSlotLayoutBehavior.TryGetLocalAnchor` returns the port's centre in the node's client coordinates; it is independent of pan, scroll and measurement timing. Do not derive the curve endpoint from `slot.Anchor − current projection`: a node leaving the visible set freezes its measurement while the projection keeps moving, and the bend direction flips.

⚙ **The canvas repaint must come after the geometry refresh.** `ApplyPanCore` writes the scroll offset first, measures the slots, refreshes the link layer, then calls `canvas.Update()` during a gesture. Reversing that order shows the previous frame's curves while dragging.

⚙ **A per-link view is still possible, but opt-in.** `WorkflowLinkAttachment.Attach(control)` owns endpoints, window-region carving, curve publishing and pointer events; your `OnPaint` draws. A carved link window must use the surface background as `BackColor`, has no stroke to widen at zero length, and needs `Invalidate()+Update()` to avoid drag trails. For a graph with more than a handful of links, use the single layer.

## Adding your own content to a node

⚙ **No `Viewbox`.** `WorkflowNodeViewBehavior` attaches `WorkflowNodeAttachment`, points drag and slot layout at the canvas, and keeps the card backdrop composited. `WorkflowNodeTitleBehavior` binds the title and scales the header with the collapse factor; `WorkflowNodeRowsBehavior` rebuilds the input/output rows from `node.Slots`.

⚙ **Content you add joins the same layout.** Put it inside the node card's `PART_*` shell and scale it in the node view's own layout code; a render transform is not part of this adapter's measurement contract.

⚙ **Ports are measured controls.** The slot layout measures each slot view and writes `slot.Anchor` in canvas-local space through `SlotAnchorFromCanvasLocal`, because the canvas client area *is* the link rendering space and a node's `Location` already includes the pan and `ActualOffset`. `SlotAnchorFromVisualCenter` subtracts `ActualOffset` a second time and shifts every link by `−ActualOffset`.

## Zoom and pan

⚙ The zoom gesture is `MouseWheel` **plus** an `Application.AddMessageFilter` on `WM_MOUSEWHEEL`, which swallows the message so no scrollable child scrolls instead. Wheel-up is `Scale *= 1 / 1.1`; `Scale` is a collapse factor.

⚙ **The scroll is clamped to the reachable range; this adapter does not grow the canvas.** A plain wheel is routed as `PointerWheelChanged` and applied through `WorkflowInput.Scroller` unless a subscriber set `PreventDefault`; Ctrl+wheel is the zoom gesture and can be refused the same way.

⚙ **A self-hosted canvas sets the virtualize inset before writing `Viewport`.** The tree has no decorator-hosted ruler to ask, so the reserve is passed explicitly (`RulerReserve` on both axes); skipping it culls nodes still visible under the ruler.

## The link context menu

⚙ **The menu is built in code, not markup.** In the tree's code-behind:

```csharp
WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, static (menu, link) =>
    menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null)));
```

The surface opens the strip on a right-press on a link, positions it, and suspends input routing while it is open. **The generated template adds no entries** — including Delete.

## Item templates — `VeloxDev.WinForms.Templates`

**Generates** `.xaml` + `.xaml.cs` for node, slot, link and tree, and a `.cs` for selector, decorator and minimap.

| Item | Shape | You supply |
|---|---|---|
| tree | `v:UserControl` + `WorkflowSurfaceBehavior`; `PART_Canvas` carries `WorkflowLinkLayer` | the palette, the selector resource, the link layer's line colour/width |
| node | `v:UserControl` + `WorkflowNodeViewBehavior` / `WorkflowNodeTitleBehavior` / `WorkflowNodeRowsBehavior` | the card shell (`PART_Card`, `PART_Header`, `PART_Title`, `PART_OutputSlots`) and its colours |
| slot | `v:UserControl` + `WorkflowSlotAttachment` | the glyph path and by-state colours |
| link | `v:UserControl` + `WorkflowLinkAttachment` — **per-link opt-in**; the default surface draws links in `WorkflowLinkLayer` | the resting curve and palette |
| minimap | `WorkflowMinimapOverlay` | the four palette colours |
| grid decorator | `Panel, IWorkflowGridDecorator` plus `SurfaceCanvas` | grid/ruler palette, spacing, major-line frequency |
| template selector | `WorkflowTemplateSelector` | which view each pooled item kind gets (node; link only for the opt-in path) |

⚙ **The tree references siblings by type name**, so generate all seven items into one namespace. The tree needs `GridDecorator` / `SurfaceCanvas` and `TemplateSelector`; the selector needs `NodeView` / `LinkView`. Missing one is a compile error, not a runtime fallback.

⚙ **The tree's selector is a resource, not a type-name property.** Declare `<local:TemplateSelector x:Key="WorkflowTemplateSelector" />` and point `behaviors:WorkflowSurfaceBehavior.TemplateSelector` at it. `NodeViewType` / `LinkViewType` still exist as a built-in type-name fallback, but the generated templates do not use them.

⚙ **Three helpers are shared by all items:** `WorkflowSurfaceColors.Parse`, `WorkflowSurfaceGraphics.RoundedRectangle`, `WorkflowSurfaceGrid` (grid-line classification and ruler-label formatting).

⚙ **The decorator is the floating ruler.** `GridDecorator.RulerBand` feeds the virtualizer and the adapter's `WorkflowRulerOverlayForm`, which draws the translucent top/left bands above the opaque node cards. `SurfaceCanvas` is only the pool host; it is not a grid decorator.

⚙ **The fully-wired pack.** Every declared parameter, including all four slot colours and `slotPath`, has a consumer here — the pack to read when you want to know what a parameter does on a framework where it is inert.

## Pitfalls

⚙ **No transparent sibling overlay for links.** A WinForms child window does not composite over its siblings; a viewport-sized transparent link layer either covers the grid or hides behind the scroll host. Paint links on the canvas instead.

⚙ **Keep `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED` in mind.** The adapter applies them through `NativeWindowStyleHelper`; the composited path is skipped above roughly a hundred descendant windows. A surface that worked at demo size can flicker at graph size.

⚙ **Do not hand-roll a second link geometry path.** The layer publishes the curve Core hit-tests; a host-side highlight must draw from `ILinkHitTestable.Curve`, or the glow and the click target disagree.
