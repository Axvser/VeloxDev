# Pitfalls — geometry, anchors and viewport

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [canvas-math.md](../practices/canvas-math.md) and [view-layer.md](../practices/view-layer.md).

## Links are invisible

**Symptom:** a connection exists in the model, but nothing is drawn.

**Cause:** the slot's `Anchor` is still `(NaN, NaN, 0)`. Core never computes a port position; only the layout behaviour writes it after measuring a registered control. The render-ready gate suppresses a link with an unmeasured end.

**Fix:** confirm the node view registers the port control through `SlotNames` / `SlotEnumeratorNames` and that the control's `DataContext` is the slot. Check `slot.Anchor.Horizontal` for `NaN`. The correspondence is described in [view-layer.md](../practices/view-layer.md#ports-the-model--view-correspondence).

## A link draws to the wrong point, or stretches into a long arbitrary curve

**Symptom:** the link exists but is attached to the wrong place, or a huge curve crosses the canvas.

**Cause:** a custom link view bound `CanRender` to `IsVisible` without the finite check. A `NaN` anchor does not hide the curve; it degenerates into long geometry. The symptom looks like a camera or layout bug.

**Fix:** start the render with `link.IsRenderReady()` (or an explicit `IsVisible && finite(anchor)` check). Check the port correspondence before the camera.

## Links vanish at deep zoom

**Symptom:** links are fine at 100% and disappear when zoomed out.

**Cause:** the renderer was handed an unbounded absolute coordinate or a whole-world canvas size and hit a platform limit (Win2D's texture cap, a renderer's layout-box culling).

**Fix:** use the platform's strategy — a viewport-sized overlay (MAUI), an offset frame (WinUI), or self-bounding geometry (Jalium). See [new-adapter.md](../practices/new-adapter.md#4-link-strategy).

## A link swallows every canvas gesture

**Symptom:** clicking anywhere on the surface selects or deletes a link, or panning stops near a link.

**Cause:** the link view's hit region is its whole canvas-sized box, not the painted stroke.

**Fix:** publish the curve you actually draw and keep the view's hit region on the stroke (a geometry with no background, or `pointer-events: stroke`). Retract with `PublishCurve(null)` on the paths that draw nothing.

## The camera drifts inward on every zoom step

**Symptom:** zooming near a canvas edge makes the content slide and then jump.

**Cause:** the zoom commit chain grew the canvas near an edge instead of clamping the final scroll, or read `ActualOffset` / `ActualSize` before the scale commit.

**Fix:** follow the five-step chain — `WorldAtViewportCenter` → pivot + scale → `EnsureNegativeCover` → relayout → `PivotCenterScroll` → clamp. Clamp the final scroll to the reachable range; growth near an edge is the drift.

## The pointer does not stay under what was grabbed while zooming

**Symptom:** zoom works, but the content moves under the pointer.

**Cause:** the pivot was read after the scale commit, or the viewport size was stale, or zoom-in multiplied `Scale` instead of dividing it.

**Fix:** read the world point before writing `Scale`; treat zoom-in as `Scale *= 1 / 1.1`; use the current viewport size.

## A node jumps when dragged, or drifts as the zoom changes

**Symptom:** the node follows the pointer at 100% and drifts at other zoom levels.

**Cause:** the drag wrote a view-space delta into world-space storage, or mutated the object returned by the `Anchor` getter. At `Scale != 1` the getter returns a copy; at `Scale == 1` it returns the live object but bypasses the spatial index and the views.

**Fix:** convert with `StandardMove` for a view-space delta, `StandardSetAnchor` for an anchor that is already world (`screen − ActualOffset`), and assign through the property setter.

## Every link is offset by a constant

**Symptom:** all links are shifted by the same amount, at every zoom.

**Cause:** the adapter used the wrong `SlotAnchor*` function. `SlotAnchorFromVisualCenter` subtracts `ActualOffset`; on a host that already applies the pan (WinUI/MAUI/WinForms), that subtracts it twice and shifts by `−ActualOffset`.

**Fix:** pick the form by the frame the measurement arrives in — see [canvas-math.md](../practices/canvas-math.md#slot-anchors--the-three-way-contract). Write the choice in a comment at the call site.

## The canvas pans in the model but not on screen

**Symptom:** nodes move in the model; the rendered canvas stays still.

**Cause:** the `RenderTransform` binding was placed on an element inside the item instead of the `DataTemplate` root. The ancestor lookup resolves against the item's own visual tree and finds nothing.

**Fix:** put the binding on the `DataTemplate` root, exactly as the GUI reference shows.

## A saved graph opens at the origin

**Symptom:** the graph loads, but the scroll position is lost.

**Cause:** the adapter persisted `ViewportOffset` but never restored it, or restored before the tree was attached.

**Fix:** implement both halves of the pair — `ViewportOffsetFromScroll` on every measured scroll, `HasViewportRestore` → `ViewportRestoreScroll` → clamp when a tree arrives. Capture the offset before the first refresh; the refresh writes `ViewportOffset` from the control's current position.

## Nodes under the floating ruler are culled

**Symptom:** nodes visibly under the ruler disappear while panning.

**Cause:** the virtualize inset was not set, or the decorator's `RulerBand` returns `0` while still covering the top/left.

**Fix:** forward `RulerBand` to `SetVirtualizeInset` before writing `Viewport`; on a self-hosted surface pass the reserve explicitly. A decorator that covers a band must report its thickness.

## A WinForms node does not move until something else repaints

**Symptom:** the model changed; the card is still drawn where it was.

**Cause:** WinForms is pull-driven. Nothing binds; the surface must be told to refresh.

**Fix:** call `WorkflowSurfaceBehavior.Refresh(host)` after a mutation. When a slot anchor must be correct in the same frame, use the adapter's `SyncNow` escape hatch.
