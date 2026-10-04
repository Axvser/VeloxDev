# Jalium

`VeloxDev.Jalium` · targets `net10.0` on `Jalium.UI.Controls` · **platform-neutral** (Windows, Linux and Android)

⚙ **Reference implementation:** `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/` — seven single `.cs` files, no markup. As of the 2026-10-03 refactor they are **thin subclasses of the adapter's base classes**: `TreeView.cs` sets the palette/port layout/grid/selector, `NodeView.cs` draws the card, `LinkView.cs` sets the stroke, `GridDecorator.cs` sets the grid palette, `SlotView.cs` holds the port-layout values, `TemplateSelector.cs` builds the factory. **The zoom itself lives in the demo's window, not in the view folder** — look there for the host-driven zoom pattern.

⚙ **The "Trimmed" in that path means *minimal demo*, not trim configuration** — the name is a demo-size choice, not a publish setting: `VeloxDev.Core`'s `net8.0` target sets `IsAotCompatible=true` and its agent surface is reflection-free, so the package declares itself usable under trimming and NativeAOT. Do not read publish-time safety into the folder name.

## Writing the surface

The adapter ships the surface as a **base class**, `Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowTreeView.cs` — it owns the pooling, the viewport bookkeeping, the zoom pin, the pan/node-drag/connection gestures, the grid+ruler rendering and hit-testing. Derive from it and set five properties:

```csharp
public sealed class TreeView : WorkflowTreeView
{
    public TreeView()
    {
        SurfaceBackground = Color.FromRgb(0x1E, 0x1E, 0x1E);
        ConnectingLinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
        PortLayout = SlotView.Layout;
        GridDecorator = new GridDecorator();
        TemplateSelector = TemplateNamespace.TemplateSelector.CreateSelector();
    }

    protected override void OnBuildLinkMenu(ContextMenu menu, IWorkflowLinkViewModel link)
    {
        // The menu is rebuilt on every right press: add or remove entries here. The base adds "Delete".
        base.OnBuildLinkMenu(menu, link);
    }
}
```

Do not re-implement the gestures, the virtualization or the router maths in your subclass — that machinery is the adapter's, not the project's.

⚙ **The link context menu is built in code.** Override `OnBuildLinkMenu(ContextMenu menu, IWorkflowLinkViewModel link)`; the base adds a Delete item, and the adapter subscribes to the interaction hub, positions the menu and reports its open and close.

⚙ **Zoom is host-driven here.** The surface has to be told when a zoom is committed:

```csharp
// after your own zoom math, at the window level
surface.NotifyZoomCommitted();
```

The adapter cannot infer it, because Jalium's `ScrollChanged` is unreliable — the surface pins the viewport to the *landing* target for a short window (`ZoomPinLifetimeMs = 250`) until the commit settles. That state machine is in `WorkflowTreeView`; do not simplify it into ordinary synchronous scroll handling.

## Canvas and node positioning

⚙ **There is no canvas render transform.** The base view positions each card at its final screen location:

```csharp
Canvas.SetLeft(this, _node.Anchor.Horizontal + OriginX);
Canvas.SetTop(this,  _node.Anchor.Vertical   + OriginY);
```

The reason is the "world == model" contract: with the views at their final position, anchors, hit-testing and link geometry can never go stale. Do not "harmonise" it with the `RenderTransform` carrier the other XAML adapters use. (The old `ViewManager` / `ViewPool` `UpdateRenderTransforms` mirror was **deleted** — there is no transform carrier to set, on the host or on the pooled views.)

## Adding your own content to a node

`WorkflowNodeView` already wraps your drawing in a `<Viewbox Stretch="Uniform">` around a design-size canvas, like the other XAML-style adapters. You **override `DrawCard`** and paint at design coordinates:

```csharp
public sealed class NodeView : WorkflowNodeView
{
    protected override void DrawCard(DrawingContext dc) { /* draw at PortLayout.DesignWidth × DesignHeight */ }
}
```

⚙ **The port glyphs are controls, not part of your drawing.** `WorkflowNodeView` hosts one `WorkflowSlotView` per port at the positions `WorkflowPortLayout` gives, and tints each by its slot's state. Restyle ports in your `slot-view` item (derive `WorkflowSlotView`, change the radius and the standby colour); the card itself draws only the chrome, the title and the output row labels.

⚙ Port centres are computed **from the model** by the adapter's `WorkflowPortGeometry` (from the slot's parent node geometry plus the design-local position scaled by `node.Size / DesignSize`). There is no coordinate host to measure against — if you change how nodes are positioned, keep that computation in step. **Assign the same `WorkflowPortLayout` instance to the surface, the cards and the link views** (the `slot-view` item's `Layout`); the surface hit-tests that layout, so a card with its own copy would put its glyphs where nobody looks for them.

## Links — self-bounding geometry

⚙ **The canvas's `Width`/`Height` must track `Layout.ActualSize`.** A stale extent means that after zooming in, links fall outside the scrollable range. `WorkflowTreeView.UpdateCanvasSize` does this; if you derive a custom surface, keep it.

⚙ **The link view keeps itself on its own curve's bounding box** (`WorkflowLinkView`): it computes the endpoints in canvas-local space, moves the element to their bounds, and `OnRender` bakes back into element-local coordinates — recomputed on every layout or endpoint change. Your subclass only supplies the stroke:

```csharp
public sealed class LinkView : WorkflowLinkView
{
    public LinkView() { LinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF); Thickness = 2; }
}
```

This matters because **Jalium's renderer culls child elements by layout box**. A full-canvas stale box makes the whole line vanish at deep zoom. If you re-template the link view, keep the self-bounding behaviour; a plain canvas-spanning element will work at 100% and disappear at 40%.

## Item templates — `VeloxDev.Jalium.Templates`

**Generates code only** — all seven items are a single `.cs` file, no XAML anywhere. All seven are now **thin subclasses of the adapter's base classes** (`WorkflowTreeView` / `WorkflowNodeView` / `WorkflowLinkView` / `WorkflowGridDecorator` / `WorkflowTemplateSelector` / `WorkflowMinimapOverlay`) or a small values/factory static class (`SlotView.Layout`, `TemplateSelector.CreateSelector()`). Siblings are referenced as **static members**, not markup aliases: the generated tree's constructor calls `SlotView.Layout`, `new GridDecorator()` and `TemplateSelector.CreateSelector()`. **Generate the selector item as well: without it the tree does not compile** (and likewise the decorator and slot items).

⚙ **All the machinery is in the base classes**: the virtualize inset (`SetVirtualizeInset(WorkflowGridDecorator.RulerThickness)`), the committed-zoom state machine (`_zoomPin`, a pinned `UpdateViewport()`), the gestures and the rendering. The host owns zoom and is expected to call `NotifyZoomCommitted()`. Keep that split when you edit it — put palette and card art in your subclass, not platform machinery.

⚙ **This pack has the most inert parameters.** Wired: node (`nodeBackground`, `nodeForeground`, `nodeBorderBrush`, `nodeBorderThickness`, `nodeCornerRadius`), link (`linkColor`, `linkThickness`), selector, tree (`surfaceBackground`), grid (`minorGridColor`, `majorGridColor`, `axisColor`, `gridSpacing`, `majorLineEvery`, `rulerBackground`, `rulerTickColor`, `rulerLabelColor`, `rulerDividerColor`). Declared-but-ignored, each carrying a "cross-GUI CLI parity" note: slot `slotBackground`, `slotColor`, `slotBorderColor`, `slotPath`; tree `surfaceBorderBrush`, `surfaceBorderThickness`, `surfaceCornerRadius`; decorator `gridBackground`; minimap `minimapBackground`, `minimapBorder`, `nodeFill`, `viewportStroke`.

⚙ **The tree item has no `linkColor` parameter** (only the link item declares that symbol) — its generated `ConnectingLinkColor` is the literal `#DDFFFFFF`, the same value the other platforms' tree items hardcode, so the seven tree packs keep identical parameter sets. Passing `-lc` to `jalium-v-tree` is not accepted; to change that colour, edit the generated line.

⚙ The decorator draws its grid at runtime from the adapter's `DrawGrid(...)`, which is why its background colour is inert, and why restyling the grid means editing the palette properties rather than passing `-bg`.

⚙ The adapter's ruler thickness `36` is a **single source** (`WorkflowGridDecorator.RulerThickness`, `const`); the surface, the node cards and the links all read it, and the template carries no copy. Do not add a `RulerReserve` constant back into the template.

⚙ **Packaging is the odd one out**: this pack's csproj sits one level higher than the other six — theirs live inside `working/`, this one beside it. The `working/content/` pack tree itself is present and the same as theirs, so the difference is the csproj's location, not a missing folder. It also omits the license expression, repository URL and reference payload the others carry. Nothing about the generated code depends on it, but a diff across packs will show it.

⚙ Verification is one script for all seven platforms now: `Src/Verification/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict` (the two older per-platform scripts are thin forwarders, so existing invocations still work). It packs, installs, generates all seven, compiles them together against the adapter, compares each against its `Examples/Workflow/Jalium Trimmed/` mirror (normalising the colour spelling first), and **fails any generated file that still contains a `replaces` placeholder token** — the assertion that catches a template referencing a symbol its pack never declares; a colour-normalising comparison alone misses that class of defect.
