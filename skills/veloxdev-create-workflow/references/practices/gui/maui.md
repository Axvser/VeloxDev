# .NET MAUI

`VeloxDev.MAUI` · targets `net10.0;net10.0-windows10.0.19041.0`

⚙ **Reference implementation:** `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/` — note `Controls/`, not `Views/`. `TreeView.xaml` is where `x:Name="Root"` and the node-only pooling live.

⚙ **"Trimmed" means *minimal demo*, not a publish setting:** [the rule in SKILL.md](../../../SKILL.md#checking-your-work).

## Writing the surface

⚙ **The host is a `ContentView`, and the settings are attached properties** — the same ones WPF uses, so [wpf.md](wpf.md#writing-the-surface) is the map. Only the namespace assembly changes:

```xml
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.MAUI"
```

```csharp
WorkflowSurfaceBehavior.SetIsEnabled(host, true);
WorkflowSurfaceBehavior.SetScrollViewerName(host, "PART_ScrollViewer");
WorkflowSurfaceBehavior.Refresh(host);                 // after a mutation
```

⚙ **The tree arrives as the host's `BindingContext`.** This adapter has no `SetWorkflowTree`; set or bind the `ContentView`'s `BindingContext` to your tree. The behaviour subscribes to `BindingContextChanged` and re-reads it from the host, or from any of the `PART_` elements.

## Adding your own content to a node

⚙ **MAUI has no `Viewbox`, and the adapter deliberately does not use a render transform.** It scales by resizing every layout metric in code — the header row height, the title and label fonts, the slot glyphs, and the slot stack's margins and spacing — because a render transform would break the coordinates the slot-layout behaviour reads.

⚙ **Scale positions as well as sizes.** The generated view multiplies every design metric by `k = collapsedWidth / 260`; leaving a spacing or margin at its design value pushes the ports outside the collapsed card, and the links then anchor to those out-of-card points.

⚙ **Content you add scales through the same code path.** Extend the node view's `OnSizeAllocated` / `ApplyScale` with your own metrics, following the same factor.

```csharp
private const double DesignWidth = 260;

protected override void OnSizeAllocated(double width, double height)
{
    base.OnSizeAllocated(width, height);
    ApplyScale(width);
}

private void ApplyScale(double width)
{
    var k = Math.Max(0.02, width / DesignWidth);
    // The generated view already scales the header, fonts, slot sizes and the stack's spacing/margins by k.
    myLabel.FontSize = Math.Max(3, 11 * k);          // sizes by k
    myLabel.Margin = new Thickness(0, 0, 10 * k, 0); // positions by k too
    myStack.Spacing = Math.Max(0, 6 * k);
}
```

⚙ **The scaling is layout-only.** A render transform would change the coordinates the anchor measurement reads; keep layout changes in `OnSizeAllocated` / `ApplyScale`.

## Links — one overlay for the whole graph

⚙ **Every link is drawn by a single `WorkflowLinkOverlay : GraphicsView` sized to the viewport, living outside the world canvas** as a sibling of the `ScrollViewer`. Endpoints are converted with the same identity the grid uses (`px = Ruler + collapsedAnchor + ContentOffset − ScrollOffset`) and out-of-bounds links are culled by bounding box.

⚙ **A `GraphicsView` the size of the canvas exceeds Win2D's texture limit (≈16k device pixels)** and the entire layer disappears silently at deep zoom. Keep the overlay viewport-sized; per-link `GraphicsView`s and canvas-sized overlays both hit the limit.

⚙ Slot anchors are measured **canvas-local**, by summing the parent chain with `AbsoluteLayout.GetLayoutBounds` (which excludes `Translation`), and written through `SlotAnchorFromCanvasLocal`. The visual-centre form subtracts `ActualOffset` twice and offsets every link.

⚙ **The travelling highlight is a property of the overlay, not of a view: `LinkFlowEnabled`**, off by default. Turning it on makes every settled link carry a band that shows which way its data flows (the rule the full demos follow — see [view-layer.md](../view-layer.md#which-way-the-data-goes)). It is the overlay's own animation, started on `Loaded` and exited on `Unloaded`, so the bands on every link advance together.

⚙ **This overlay strokes the band as a run of short pieces.** `ICanvas` in the MAUI version this ships against has `SetFillPaint` and no stroke equivalent, so a gradient stroke is unavailable; the piecewise band also follows a link's curve, which a straight-axis gradient stroke cannot.

⚙ **Read `ScrollX` / `ScrollY`, the measured values** — an animated or clamped scroll makes the requested value a lie until the next frame. Guard `ScrollViewer.Width` / `Height` for `NaN` before using them as a viewport size.

## Zoom

⚙ A `PinchGestureRecognizer` for touch, plus a `#if WINDOWS` platform wheel hook. **Keep the Windows `ScrollViewer`'s `ManipulationMode` as the adapter sets it** — the adapter demotes it to a passive container with `TranslateX | TranslateY | Scale`; `None` breaks MAUI's own gestures.

⚙ The platform element has no `DataContext`. A platform-level handler recovers the tree from the host in a closure (`host.BindingContext as ITreeComponent`).

## The connection preview and the wheel

⚙ **The candidate receiver is reported from the slot-connection behaviour, not the surface.** `WorkflowSlotConnectionBehavior.UpdatePointer` calls `tree.StandardSetPreviewReceiver(SlotHitTestEx.HitTest(tree, x, y))` while the drag is active, because this adapter's input surface is an overlay: the visual under the pointer cannot answer "which slot". The stage lands on the virtual link (`IConnectionPreviewLink.Preview`) and the slot's `PreviewReceiver` bit.

⚙ **The wheel scrolls through `WorkflowInput.Scroller`.** The surface registers the adapter's `SurfaceScroller` while attached; a plain wheel is routed first and applied only when no subscriber refused it. Ctrl+wheel is the platform zoom path.

## Item templates — `VeloxDev.MAUI.Templates`

**Generates** `.xaml` + `.xaml.cs` for node, slot, link and tree, and a `.cs` for selector, decorator and minimap.

⚙ **`x:Name="Root"` is required on the link view and the tree view.** It is the binding source for the inner overlay — `{Binding WorkflowTree, Source={x:Reference Root}}`, `InteractionSource="{x:Reference Root}"`, and the same for `ScrollOffsetX/Y`, `ContentOffsetX/Y`, `RulerThickness`, `LinkLineColor`, `VirtualLineColor` and `StrokeWidth`. **`InteractionSource` is what makes links interactive**: the overlay itself is input-transparent, so it hooks the element you name there; leave it unset and links draw but cannot be hovered or deleted. Node and slot views do not need it.

⚙ **The tree declares no `LinkTemplate` and the link view is a thin forwarding shell.** The generated link view forwards its properties to the adapter's sealed `WorkflowLinkOverlay`, and the tree's template selector carries a `NodeTemplate` only — links are not pooled as views. A pack that still ships a per-link `LinkTemplate` is behind the demo; copy `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/TreeView.xaml` instead.

⚙ **`slotPath` (`-sp`) is accepted and discarded on this pack.** The generated slot view is an `Ellipse` whose fill and stroke bind the slot's `State` through `SlotStateBrushConverter`; the shared SVG path is not rendered by this GUI's `Path` parser. Change the generated slot view when you need a different glyph. `StrokeShape="RoundRectangle TemplateNodeCornerRadius"` substitutes a `Shape`, not a numeric `CornerRadius`.

⚙ **The link context menu is a `MenuFlyout` resource plus `LinkMenuKey`,** declared on the tree and pointed at by `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu"`. On Windows the adapter turns it into a native flyout; on the other platforms it materializes its own overlay layer over the surface (positioned by `Margin`). Each `MenuFlyoutItem` is bound to the pressed link, so `Command="{Binding DeleteCommand}"` is a complete entry.
