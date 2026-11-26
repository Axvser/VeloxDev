# Shared pipelines

Each section is a sequence the adapter joins at a named seam. The arithmetic and state machines are in Core; the adapter supplies one or two narrow interfaces. Do not copy the sequence into the adapter.

## 1. Input → route → default action

```text
platform event
  → translate to Pointer*EventArgs / Key*EventArgs
      Position  = canvas-local Anchor (scroll − ActualOffset), Layer = source view's layer
      Source    = the view that caught it (or the surface)
      Target    = ResolveTarget(hit) — node/slot from DataContext, else HitTestVisibleLinks
      Handle    = new WorkflowEventHandle()
  → WorkflowInput.For(tree).Route(args)
      chain: target → ancestors → tree (tree always last)
      Entered/Exited diffed first when the target changes
      handlers run before the framework reaction
  → adapter's default action runs unless Handle.PreventDefault
```

⚙ **Resolve the target from the live visual tree when one exists, and from published curves when it does not.** A node or slot answers from its `DataContext`; a link only answers from `tree.HitTestVisibleLinks(x, y, input.HitRadius)`, because one overlay may have drawn every link.

⚙ **Route every pointer action that carries a target, not only move/enter.** `PointerTarget` is what the Delete key applies to; a wheel turn with no move in between must still keep it current.

⚙ **A key has no position.** Pass `input.PointerTarget` as the key's `Target`, and make sure the surface (or the hovered link view) actually holds keyboard focus. On WPF/`UserControl`, `Focusable` defaults to `false` — the adapter turns it on.

⚙ **The wheel default is the adapter's.** Keep the platform's scroll container from scrolling the canvas, route the notch, then:

```csharp
var wheel = TranslateWheel(platformArgs);        // VeloxDev.WorkflowSystem.PointerWheelEventArgs
input.Route(wheel);
platformArgs.Handled = true;
if (!wheel.Handle.PreventDefault)
    input.Scroller?.ScrollBy(wheel.DeltaX, wheel.DeltaY);
```

Register `Scroller = new SurfaceScrollerCore(host)` and `Navigator = scroller` when the surface attaches; on detach clear them only if they are still yours.

⚙ **The connection preview is geometry, not hits in the visual tree.** While `tree.VirtualLink.IsVisible`, move the pointer with `tree.SetPointerCommand.Execute(anchor)` and report the candidate with `tree.StandardSetPreviewReceiver(SlotHitTestEx.HitTest(tree, x, y))`. Keep hover routing off during the drag so every crossed link does not light up. An asynchronous verdict lands through `StandardRefreshConnectionPreview`.

⚙ **`PreventDefault` on a press suppresses the adapter's default gesture.** The adapter must read the press handle *after* the route; the WPF surface stores it and the node-drag / slot-connection behaviors read it. Without that read, a host cannot veto a drag or a connection.

## 2. Measure → slot anchor → layout

```text
platform notification (layout / model / configuration / viewport / attachment)
  → pipeline.RequestMeasure(trigger)
  → SlotLayoutPipelineCore.Measure() on the UI thread
      for each configured part, in declared order:
          part = locator.FindNamedSlot / FindEnumeratedSlots
          if part.IsMeasured and coordinate host resolved:
              center = part.MeasureCenter(host)
              anchor = WorkflowSurfaceMath.SlotAnchorFromVisualCenter | FromCanvasLocal | FromNode
          write slot.Anchor only when it moved by more than the tolerance
```

⚙ **`SlotCoordinateSpace` selects the conversion; it is not a style choice.**

| `MeasureCenter` returns… | Space | Conversion |
| --- | --- | --- |
| a centre still in scroll space (includes `ActualOffset`) | `ScrollSpace` | `SlotAnchorFromVisualCenter` |
| a centre already in canvas-local space | `CanvasLocal` | `SlotAnchorFromCanvasLocal` |
| a node-local centre, or no coordinate host | `NodeLocal` | `SlotAnchorFromNode` |

⚙ **The layout pass is the cheap path.** `SlotLayoutTrigger.Layout` compares `WorkflowLayoutStamp`; unchanged geometry, size, slot references and realized-container identity skip the tree walk. A pass that did not find every slot does not certify the stamp. Every other trigger is unconditional and deferred. Call `RequestMeasure(SlotLayoutTrigger.Viewport)` when a zoom/scroll transform changed without a model change.

⚙ **Parts are live handles.** Resolve them on every `Measure`; a template rebuild invalidates the old part. The stamp folds the realized-container identity and the `Node.Slots` reference hash, so a same-count rebuild or slot swap re-measures; an adapter without `ISlotPartIdentityLocator` falls back to the count.

⚙ **The batch is per tree.** `SlotLayoutPipelineCore` creates or reuses `WorkflowLayoutBatch.For(tree, host.Post)` and drops the queue when the node changes trees. `Stop()` on detach makes queued work a no-op; `CancelPending()` is the temporary-unload variant.

## 3. Surface metrics → scroll → viewport restore

```text
platform scroll/resize/pan
  → ReadMetrics() from the scroll container and decorators
  → SurfaceMetrics.Create(scrollX, scrollY, contentX, contentY,
                          viewportW, viewportH, surfaceW, surfaceH,
                          rulerBand, vbar, hbar, margin)
  → SurfaceMetricsPush.PushGrid(metrics, decorator, tree)
     SurfaceMetricsPush.PushMinimap(metrics, minimap, tree)
     SurfaceMetricsPush.PushViewport(metrics, tree)
```

⚙ **`PushViewport` is the only writer of `ITreeHelper.Viewport`.** It computes `local = scroll − ActualOffset`, writes `Viewport(localX, localY, width, height)`, and persists `Layout.ViewportOffset = (localX, localY)` for serialization.

⚙ **Restore is a state machine, not a one-line scroll assignment.**

```text
new tree attached
  → restore.Capture(tree)          // read Layout.ViewportOffset, keyed on the tree
  → …first PushViewport…           // this overwrites ViewportOffset, so Capture must precede it
  → restore.Schedule(host, () => PushViewport again)
      host.DeferRestore runs when the platform has laid the tree out
      clamp pending position to the fresh maximum, ApplyOffset
      if the platform is not ready, leave the pending position for the next pass
```

⚙ **Scroll arithmetic stays in Core.** `SurfaceScrollerCore.ScrollBy` converts the wheel profile, writes only the axis that moved, and on overscroll calls `WorkflowSurfaceMath.ClampScrollOffset` then `ISurfaceScrollHost.ExpandCanvas()` before re-reading the maximum. `CenterOn` uses `CenterScroll` and the same growth semantics. The adapter only reports offsets/limits and performs the write.

⚙ **`ApplyOffset` takes nullable axes.** A wheel turn that only moved vertically must pass `null` for horizontal, or the platform clamps the other axis back into range.

⚙ **Surface parts go through `SurfacePartRegistry`.** Set each role as it resolves (`WorkflowSurfacePart`: `ScrollViewer`, `Canvas`, `GridDecorator`, `MinimapOverlay`, `PointerPressSource`); the registry compares by reference and reports `(old, new)` once. Bind subscriptions with `SurfacePartBinding<T>` — it detaches the old part before attaching the new one and releases the current one on `Dispose` — and on detach dispose the bindings and call `Clear()`.

## 4. Link publish → hit test

```text
view draws a link
  → if (!link.IsRenderReady()) return;              // endpoints still NaN → draw nothing
  → curve = LinkCurve.BuildLinkCubic(link, startX, startY, endX, endY, pullMinimum)
  → link.PublishCurve(curve, this);                  // this = visual, or null on a single overlay
surface pointer move
  → tree.HitTestVisibleLinks(x, y, input.HitRadius)
```

⚙ **The published curve is the only evidence of what was painted.** `HitTestVisibleLinks` walks `VisibleItems` from the back, skips the tree's `VirtualLink`, and rejects a link covered by a node card (except within reach of its own ports). It never asks whether the endpoints were measured; a view that draws nothing publishes `null`.

⚙ **Publish in the space you will query.** Curves, slot anchors and pointer positions must share the canvas-local frame. A view that draws in a transformed overlay must publish the same transformed polyline it painted.

⚙ **Retract on rebind and unload.** A pooled view whose `DataContext` changes must `PublishCurve(null)` for the old link before drawing the new one.

⚙ **Use `LinkCurve.LinkCurvePoints` for the shape.** It derives each control point from the port's own node edge, so a port on a top/bottom edge and a link whose ends are reversed draw correctly. The demo's `BuildCurve` and the published curve come from the same points.

## 5. Transition prepare → start → track → execute → held

```text
ThemeManager.Transition<T>(effect)
  → InterpolatorCore.CreateScheduler(target, effect)   // theme-switch seam; a normal Transition<T> closes over its scheduler
        adapter returns TransitionSchedulerCore<Host, Interpreter, TPriority>.FindOrCreate(target)
        or null when this effect is not the platform's type
  → the switch tracks its run before Execute           // Execute finds it by its token source
  → scheduler.Execute(interpolator, state, effect, run.Cts)
        host.PostAsync(target, Awake, effect.Priority) // false → drop the animation
        interpolator.Prepare(target, state, effect, host)
            read each start on the owning thread
            resolve sampler (override → registry → ISampleable)
            normalize start/end once per segment
        interpreter loop
            timeline anchor → eased t → SamplerSet.Apply(target, t, priority)
            FPS is a maximum sample rate
            final frame is exactly the endpoint
  → write held end values for properties with no sampler
```

⚙ **`Prepare` is the settlement point.** Override it only to change the sampler-resolution chain, and call the base. `NormalizeStart` / `NormalizeEnd` run once per segment; `InsertFrame` once per frame. Never mutate the start/end arguments — they are shared with the snapshot.

⚙ **The scheduler is filed by `FindOrCreate`.** A directly constructed scheduler animates but cannot be paused, seeked or exited by target. `CreateScheduler` returning `null` is the honest answer for "this effect does not belong here"; Core then switches without animating.

⚙ **A missing sampler is a held value, not a failure.** The property keeps its old value through the switch and receives the end value at the end. A theme switch with no platform interpolator takes the same path for every property.

⚙ **The interpreter owns the frame clock.** `CreateFramePacer` should derive its wait from `affinity.ThreadFor(target)` so the loop and the writes stay on the same thread. A null pacer is supported and falls back to the thread-pool timer; every write then marshals itself.

## 6. Theme switch

```text
App startup (once)
  → ThemeManager.SetPlatformInterpolator(new Interpolator())
  → ThemeManager.SetCurrent<Dark>()

element construction
  → [ThemeConfig] generated cache + InitializeTheme()
  → IThemeValueConverter.Convert(...) turns stored context into the property type

ThemeManager.Transition<Light>(TransitionEffects.Theme)
  → cancel the switch in flight
  → ExecuteThemeChanging on every registered IThemeObject
  → one shared ITimeSourceControl; per target:
        write start values according to StartModel
        scheduler = interpolator.CreateScheduler(target, effect)
        Execute(...)
  → all landed? hold missing-sampler end values + Current = Light + ExecuteThemeChanged
```

⚙ **A converter is per adapter, not shared.** Name the value types your framework declares; register a sampler for those types or the switch holds them. `IThemeValueConverter` needs a public parameterless constructor.

⚙ **`CreateScheduler` is the same seam as in §5 and the same priority type.** Returning null makes the switch instant; so does not calling `SetPlatformInterpolator` at all.

⚙ **`Jump` needs neither the interpolator nor the platform effect type.** It writes the end values directly and advances `Current`. Use it for "switch now"; use `Transition` for "animate".

## 7. Minimap

```text
surface pass
  → SurfaceMetricsPush.PushMinimap(metrics, overlay, tree)   // offsets, viewport, available corner, tree
  → overlay render
        pipeline.Render(surface)
            Refresh() when dirty → mapping recomputed
                ContentBounds = union of node anchors/sizes
                ViewportBounds = ToCanvasLocal(scroll) + viewport size
                MinimapFit → mapped nodes + mapped viewport
            Renderer.Render(surface, context)

pointer on minimap
  → inputHost.PointerPosition = local point; IsPressed = true
  → pipeline.PointerPressed() → HitTest → mapped scroll → Viewport.ApplyScroll
```

⚙ **Debounce is built in.** Node/link/viewport changes only set the dirty flag and raise `RenderRequested`; the mapping and placement run once on the next `Render` or `Refresh`. A burst of property changes costs one mapping pass.

⚙ **Marshal `Invalidate` and render calls to the host thread.** Tree notifications can arrive from the tick thread. This is the pipeline's only thread-affinity requirement.

⚙ **The minimap element stretches.** `MinimapDrawContext.AvailableRight/Top/Bottom` plus `MinimapMargin` place it in the free top-right corner; the element itself should not carry a top-right margin.

⚙ **Do not implement node/link rendering twice.** Override the platform drawing hook that receives `MinimapDrawContext`; the mapped rectangles already carry the minimum thumb size.

## What a new adapter implements vs. inherits

| Pipeline | Implements | Inherits |
| --- | --- | --- |
| Input | translation, target resolution, default action, focus | route, bubbling, hover diff, handles, preview state |
| Slot layout | `ISlotPartLocator`, `ISlotMeasurementHost` | measurement order, tolerance, triggers, batching, stamp |
| Surface | `ISurfaceMetricsSource`, `ISurfaceScrollHost`, `ISurfaceViewportRestoreHost` | metrics snapshot, scroll/clamp/grow, `SurfacePartRegistry` + bindings, restore state machine |
| Links | view drawing + `PublishCurve` | curve sampling, hit test, node coverage, render-ready gate |
| Minimap | `IMinimapRenderer`, `IMinimapViewportHost`, `IMinimapInputHost` | subscription, debounce, mapping, placement, drag math |
| Transition | inspector, interpreter, pacer, samplers, `CreateScheduler` | sampling loop, endpoint normalization, scheduling, held values |
| Theme | converters, startup registration | switch orchestration, caches, timeline, held values |
