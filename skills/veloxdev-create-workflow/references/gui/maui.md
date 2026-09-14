# .NET MAUI

`VeloxDev.MAUI` · targets `net10.0;net10.0-windows10.0.19041.0`

⚙ **Reference implementation:** `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/` — note `Controls/`, not `Views/`. `TreeView.xaml` is where `x:Name="Root"` and the node-only pooling live, and it is the file to copy when your generated pack disagrees with it.

⚙ **The "Trimmed" in that path means *minimal demo*, not trim configuration** — the library is **not** AOT- or trim-safe (`IsTrimmable=false`, and the animation path compiles expression trees at runtime). Do not read publish-time safety into the folder name.

## Writing the surface

⚙ **The host must be a `ContentView`, and the settings are attached properties** — the same ones WPF uses, so [wpf.md](wpf.md#writing-the-surface) is the map. Only the namespace assembly changes:

```xml
xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.MAUI"
```

```csharp
WorkflowSurfaceBehavior.SetIsEnabled(host, true);
WorkflowSurfaceBehavior.SetScrollViewerName(host, "PART_ScrollViewer");
WorkflowSurfaceBehavior.Refresh(host);                 // after a mutation
```

⚙ **The tree arrives as the host's `BindingContext`.** There is no `SetWorkflowTree` on this adapter — that method exists only on the WinForms one, which has no binding system to carry the tree. Set or bind the `ContentView`'s `BindingContext` to your tree; the behaviour subscribes to `BindingContextChanged` and re-reads it from the host, or from any of the `PART_` elements.

## Adding your own content to a node

⚙ **MAUI has no `Viewbox`, and the adapter deliberately does not use a render transform.** It scales by measuring and resizing in code — the header row height, the title and label font sizes and the slot glyphs — because a render transform would break the coordinates the slot-layout behaviour reads.

If you add content to a node, **it will not scale with the zoom by itself.** Extend the node view's `OnSizeAllocated` / `ApplyScale` with your own sizes, following the factor the view already computes (`collapsedWidth / 260`, where 260 is that node's design width).

⚙ **Do not "fix" this with a render transform.** The comment in `NodeView` is explicit: layout-only changes, no transforms. Switching to a transform breaks the anchor measurement.

## Links — one overlay for the whole graph

⚙ **Every link is drawn by a single `WorkflowLinkOverlay : GraphicsView` sized to the viewport, living outside the world canvas** as a sibling of the `ScrollViewer`. Endpoints are converted with the same identity the grid uses (`px = Ruler + collapsedAnchor + ContentOffset − ScrollOffset`) and out-of-bounds links are culled by bounding box.

⚙ **This is not a style choice.** A `GraphicsView` the size of the canvas exceeds Win2D's texture limit (≈16k device pixels) and the entire layer disappears silently at deep zoom. Do not replace the overlay with a per-link `GraphicsView`, and do not size the overlay to the canvas.

⚙ Slot anchors are measured **canvas-local**, by summing the parent chain with `AbsoluteLayout.GetLayoutBounds` (which excludes `Translation`), and written through `SlotAnchorFromCanvasLocal`. Using the visual-centre form subtracts `ActualOffset` twice and offsets every link.

⚙ **Read `ScrollX` / `ScrollY`, never the requested scroll target** — an animated or clamped scroll makes the requested value a lie until the next frame. And guard `ScrollViewer.Width` / `Height` for `NaN` before using them as a viewport size.

## Zoom

⚙ A `PinchGestureRecognizer` for touch, plus a `#if WINDOWS` platform wheel hook. **Do not set `ManipulationMode = ManipulationModes.None`** on the Windows `ScrollViewer` — the adapter demotes it to a passive container with `TranslateX | TranslateY | Scale`, and `None` breaks MAUI's own gestures.

⚙ The platform element has no `DataContext`. If you write your own platform-level handler, recover the tree from the host in a closure (`host.BindingContext as IWorkflowTreeViewModel`) — a handler written the WPF way finds nothing.

## Item templates — `VeloxDev.MAUI.Templates`

**Generates** `.xaml` + `.xaml.cs` for node, slot, link and tree, and a `.cs` for selector, decorator and minimap.

⚙ **`x:Name="Root"` is required on the link view and the tree view.** It is the binding source for the inner overlay — `{Binding WorkflowTree, Source={x:Reference Root}}` and the same for `ScrollOffsetX/Y`, `ContentOffsetX/Y`, `RulerThickness`, `LinkLineColor`, `VirtualLineColor` and `StrokeWidth`. Remove it and every one of those bindings resolves to nothing. Node and slot views do not need it.

⚙ **The tree declares no `LinkTemplate` and the link view is a thin forwarding shell.** The generated link view forwards its properties to the adapter's sealed `WorkflowLinkOverlay`, and the tree's template selector carries a `NodeTemplate` only — links are no longer pooled as views. If a version of this pack still gives you a per-link `LinkTemplate`, it is behind the demo: copy `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/TreeView.xaml` instead.

⚙ **`slotPath` (`-sp`) is accepted and discarded**, and here it is genuinely meaningless rather than merely unwired — MAUI draws its ports from `SlotState` geometry, not from an SVG path. `StrokeShape="RoundRectangle TemplateNodeCornerRadius"` substitutes a `Shape`, not a numeric `CornerRadius`.
