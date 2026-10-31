# Pitfalls — input, links and the surface

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [input.md](../practices/input.md), [view-layer.md](../practices/view-layer.md) and [canvas-math.md](../practices/canvas-math.md).

## The canvas does not pan

**Symptom:** node positions change in the model; the rendered items stay still.

**Cause:** the pan `RenderTransform` binding is on an element inside the item, not on the `DataTemplate` root. The ancestor lookup resolves against the item's own tree and finds nothing.

**Fix:** move the binding to the `DataTemplate` root. On Avalonia, also check that the ancestor type in the binding path is assembly-qualified.

## A click near a link misses

**Symptom:** the pointer is next to a painted line, but the link does not answer.

**Cause:** the curve was never published, was retracted, or the point is outside the hit radius; a node card may also be covering the point; the coordinates may be in a different space than the published curve.

**Fix:** publish the curve you draw, retract only on the paths that draw nothing, and pass `WorkflowInput.HitRadius` when resolving. Check that the curve and the query are in the same coordinate space; check the node-occlusion rule and the link's own ports.

## A link answers over its whole canvas-sized box

**Symptom:** clicking anywhere selects the link; panning stops near it.

**Cause:** the view's hit region is the whole box, not the stroke.

**Fix:** keep the hit region on the painted geometry (no background fill, `pointer-events: stroke`) and publish the curve for hit-testing.

## The wheel scrolls the wrong surface, or does not scroll at all

**Symptom:** the wheel scrolls a child control, the page, or nothing.

**Cause:** the adapter's wheel hook did not receive the event (a child consumed it), or a subscriber set `PreventDefault` and did not call `Scroller.ScrollBy`, or the delta sign was inverted.

**Fix:** take the wheel at the adapter's hook; when you refuse the default, scroll through `WorkflowInput.Scroller`; remember `DeltaY` positive is away from the user while `ScrollBy`'s X positive is towards the content on the right.

## Ctrl+wheel does not zoom

**Symptom:** the wheel scrolls instead of zooming.

**Cause:** the zoom handler is attached in the wrong routing phase (WinUI has no preview phase; attach on the Canvas with `handledEventsToo: true`), or a `PreventDefault` on `PointerWheelChanged` suppressed both the scroll and the zoom.

**Fix:** follow the platform's hook in [new-adapter.md](../practices/new-adapter.md#5-zoom-hook). The zoom is routed first; a refusal of the wheel refuses both.

## A key chord does not reach the tree

**Symptom:** Ctrl+Z works over one component and not another, or only after clicking the canvas.

**Cause:** the subscription is on a component's helper instead of the tree's, or the surface never took focus, or a `TextBoxBase` handled the key first.

**Fix:** subscribe the tree helper — the route always ends there. Let the surface take focus on press outside a text editor. For a locally owned chord, set `StopPropagation` in a nearer subscriber.

## Hover stays on a link after the pointer leaves it

**Symptom:** a link stays highlighted while the pointer is over a popup.

**Cause:** hover bookkeeping ran while the popup was open.

**Fix:** set `WorkflowInput.IsSuspended` while the menu is up and clear it on close. The route still delivers events; only `PointerTarget` pauses.

## The connection preview does not follow the pointer

**Symptom:** the rubber band draws, but the candidate slot never lights up or the stage stays `Started`.

**Cause:** the adapter is not feeding the candidate, or an async check never refreshes its verdict.

**Fix:** on every pointer move while `VirtualLink.IsVisible`, call `StandardSetPreviewReceiver(SlotHitTestEx.HitTest(tree, x, y))`. When an asynchronous verdict lands, call `StandardRefreshConnectionPreview()`; the same candidate is not re-validated every frame.

## A `PreviewValidating` rejection cancels the drag

**Symptom:** the drag ends when a host rejects a candidate.

**Cause:** the host treated `e.Reject` as a release-time veto and also cancelled the gesture.

**Fix:** `Reject` reddens the candidate for this preview only; the drag continues and the release-time `Connecting` / `ValidateConnection` decision is unchanged. Cancel nothing in the handler.

## Links draw but cannot be hovered or deleted on MAUI

**Symptom:** the overlay paints, but no pointer interaction reaches a link.

**Cause:** the link view's `InteractionSource` is unset. The overlay itself is input-transparent; it hooks the element named there.

**Fix:** keep `x:Name="Root"` and bind `InteractionSource="{x:Reference Root}"` (or the tree view) on the link view. See [gui/maui.md](../practices/gui/maui.md#item-templates--veloxdevmauitemplates).

## A link menu acts on the wrong link

**Symptom:** the menu opens for one link, but its command runs against another or none.

**Cause:** the menu's data context was not set to the pressed link (WinUI's `MenuFlyout` has no `DataContext` and needs per-item assignment), or the link left the tree while the menu was open.

**Fix:** bind each entry to the pressed link; close the menu when `GetHelper().LinkRemoved` names that link. On WinUI, feed each `MenuFlyoutItem.DataContext` individually.

## A pooled link keeps animating its previous link

**Symptom:** a recycled link shows the old flow, hover or curve.

**Cause:** the view was rebound without unsubscribing from the tree's flow clock or the old component's input events.

**Fix:** unsubscribe on detach / rebind, retract the published curve with `PublishCurve(null)`, and reset the view's own state (highlight, flow scalars) when the `DataContext` changes.

## The flow band stutters while the canvas moves

**Symptom:** the travelling highlight jumps or freezes during pan or zoom.

**Cause:** a second writer is re-seating the band's position while the flow cycle owns it.

**Fix:** let the flow cycle own the offsets; re-aim the gradient and re-colour the shoulders when the endpoints change, without writing the phase. See [the animation skill's flow reference](../../../veloxdev-create-animation/references/practices/flow.md).
