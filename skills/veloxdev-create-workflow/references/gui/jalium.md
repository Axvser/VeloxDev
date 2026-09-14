# Jalium

`VeloxDev.Jalium` · targets `net10.0` on `Jalium.UI.Controls` · **platform-neutral** (Windows, Linux and Android)

⚙ **Reference implementation:** `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/` — seven single `.cs` files, no markup. `TreeView.cs` carries the committed-zoom state machine, `LinkView.cs` the self-bounding geometry, `GridDecorator.cs` and `MinimapOverlay.cs` the two Jalium-only overlays. **The zoom itself lives in the demo's window, not in the view folder** — look there for the host-driven zoom pattern.

⚙ **The "Trimmed" in that path means *minimal demo*, not trim configuration** — the library is **not** AOT- or trim-safe (`IsTrimmable=false`, and the animation path compiles expression trees at runtime). Do not read publish-time safety into the folder name.

## Writing the surface

The attached properties match WPF, with one addition — zoom is an explicit call:

```csharp
WorkflowSurfaceBehavior.ZoomBy(host, viewModel, factor);
```

⚙ **Zoom is host-driven here.** The surface has to be told when a zoom is committed:

```csharp
// after your own zoom math, at the window level
surface.NotifyZoomCommitted();
```

The adapter cannot infer it, because Jalium's `ScrollChanged` is unreliable — the surface pins the viewport to the *landing* target for a short window (`ZoomPinLifetimeMs = 250`) until the commit settles. Do not simplify that into ordinary synchronous scroll handling.

## Canvas and node positioning

⚙ **There is no canvas render transform.** Views are positioned at their final screen location:

```csharp
Canvas.SetLeft(view, collapsedAnchor.Horizontal + ActualOffset.Horizontal);
Canvas.SetTop(view,  collapsedAnchor.Vertical   + ActualOffset.Vertical);
```

The reason is written into the adapter: with the views at their final position, anchors, hit-testing and link geometry can never go stale. Do not "harmonise" it with the `RenderTransform` carrier the other XAML adapters use — `ViewManager` mirrors any transform onto the pooled views instead, so a transform set on the host never reaches them.

## Adding your own content to a node

`<Viewbox Stretch="Uniform">` around design-size content, as on the other XAML-style adapters — put your controls inside the design canvas.

⚙ The `ScaledCenter` helper the Trimmed tree and the templates use to find a port's centre **exists only in Jalium**. It is not a shared type; do not go looking for it in Core or in another adapter.

⚙ Port centres are computed **from the model** — `Canvas.GetLeft/Top` plus half the size — written through `SlotAnchorFromNode`. There is no coordinate host to measure against, so if you change how nodes are positioned you must keep that computation in step.

## Links — self-bounding geometry

⚙ **The canvas's `Width`/`Height` must track `Layout.ActualSize`.** A stale extent means that after zooming in, links fall outside the scrollable range. The Trimmed tree does this in `UpdateCanvasSize`; if you write your own surface, do the same.

⚙ **The link view keeps itself on its own polyline's bounding box**: it computes the endpoints in canvas-local space, moves the element to their bounds, and `OnRender` bakes back into element-local coordinates — recomputed on every layout or endpoint change.

This matters because **Jalium's renderer culls child elements by layout box**. A full-canvas stale box makes the whole line vanish at deep zoom. If you re-template the link view, keep the self-bounding behaviour; a plain canvas-spanning element will work at 100% and disappear at 40%.

## Item templates — `VeloxDev.Jalium.Templates`

**Generates code only** — all seven items are a single `.cs` file, no XAML anywhere. Siblings are referenced as **static members**, not markup aliases: the generated tree calls `GridDecorator.RulerThickness`, `SlotView.DesignWidth`, `SlotView.InputPortX`, `GridDecorator.DrawGrid(...)` and `ViewPool.SetTemplateSelector(this, TemplateSelector)`.

⚙ **The tree template wires the virtualize inset and the committed-zoom state machine for you** (`SetVirtualizeInset(GridDecorator.RulerThickness)`, `_zoomPin`, a pinned `UpdateViewport()`), and **removes the demo's `Ctrl`+`+`/`-` key handler** — the host owns zoom and is expected to call `NotifyZoomCommitted()`. Keep that split when you edit it.

⚙ **This pack has the most inert parameters.** Wired: node (all six), link (all three), selector. Declared-but-ignored, each carrying a "cross-GUI CLI parity" note: slot `slotBackground`, `slotColor`, `slotBorderColor`, `slotPath`; tree `surfaceBorderBrush`, `surfaceBorderThickness`, `surfaceCornerRadius`; decorator `gridBackground`; minimap `minimapBackground`, `minimapBorder`, `nodeFill`, `viewportStroke`.

⚙ The decorator draws its grid at runtime from `DrawGrid(...)`, which is why its background colour is inert, and why restyling the grid means editing that call rather than passing `-bg`.

⚙ The generated tree also disables the default virtualize inset path if you replace `GridDecorator` with your own type — check `RulerThickness` before you do.

⚙ **Packaging is the odd one out**: this pack's csproj sits one level higher than the other six — theirs live inside `working/`, this one beside it. The `working/content/` pack tree itself is present and the same as theirs, so the difference is the csproj's location, not a missing folder. It also omits the license expression, repository URL and reference payload the others carry. Nothing about the generated code depends on it, but a diff across packs will show it.
