# Adapter pitfalls

Symptom → cause → fix. Every entry is a failure this repository has hit while moving the seven adapters onto the shared pipelines, or a rule the shared code exists to enforce.

## Every link is offset by a constant; ports sit away from their visuals

**Symptom.** Perfect at the origin, uniformly wrong everywhere else. No exception, no log, no visual clue beyond the offset.

**Cause.** The adapter declared the wrong `SlotCoordinateSpace`, or used `WorkflowSurfaceMath.SlotAnchorFromNode` where a measured centre was available. A scroll-space centre (`TranslatePoint` into a canvas that still includes `ActualOffset`) fed to `SlotAnchorFromCanvasLocal` keeps the offset; a canvas-local centre fed to `SlotAnchorFromVisualCenter` subtracts it twice.

**Fix.** Match the conversion to what `ISlotPart.MeasureCenter` actually returns: `ScrollSpace` → `SlotAnchorFromVisualCenter`, `CanvasLocal` → `SlotAnchorFromCanvasLocal`, node-local → `SlotAnchorFromNode`. Write the decision in a comment at the call site, as the shipped adapters do. See [pipelines.md §2](../practices/pipelines.md#2-measure--slot-anchor--layout).

## The theme switch is instant although `Transition` was requested

**Symptom.** `ThemeManager.Transition<Light>(effect)` completes, `Current` changes, nothing animates, and nothing is logged.

**Cause.** One of two seams is missing. Either startup never called `ThemeManager.SetPlatformInterpolator(new Interpolator())`, or the adapter's `CreateScheduler` tests for a different priority type than the scheduler it constructs. The priority type appears in both places and they must agree.

**Fix.** Call `SetPlatformInterpolator` once before any element registers. Implement:

```csharp
public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
    => effect is ITransitionEffect<DispatcherPriority>
        ? TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherPriority>.FindOrCreate(target)
        : null;
```

Use `FindOrCreate` — only that path files the scheduler under the target, which is what later `Pause` / `Seek` / `Exit(target)` read.

## A theme property jumps instead of animating (one property only)

**Symptom.** The rest of the switch animates; one property snaps to its end value after the run.

**Cause.** No sampler is registered for the property's declared type. `InterpolatorCore.Prepare` reports `Unsampled` and skips it; `ThemeManager` writes the held value at the end.

**Fix.** Register the type users will *declare*, not a concrete subclass: the lookup walks exact type → base classes → interfaces, never down. WPF registers `typeof(Effect)` because `UIElement.Effect` is declared as `Effect`; registering `DropShadowEffect` would leave the declared property unreachable. A more general registration must handle the whole family.

## An animation runs at two frames, or never stops

**Symptom.** A transition writes two frames and freezes, or the pacer keeps firing after the run ended.

**Cause.** Host timer semantics differ. MAUI's `IDispatcherTimer` fires once when non-repeating; WinUI's `DispatcherQueueTimer` re-arms correctly with `IsRepeating=false`. A pacer that never fires parks the loop; one that fires twice double-samples.

**Fix.** Create the timer with the repetition your host actually needs, re-arm once per `Arm`, stop it in `Disarm`, and release it in `Dispose`. The interpreter calls `CreateFramePacer` once per run, synchronously on the thread that started the loop, so that is the moment to capture the dispatcher.

## Every frame becomes a dispatch, or work runs on the wrong thread

**Symptom.** Animations are correct but expensive; UI callbacks run on a pool thread; a host queue that is gone leaves a hung await.

**Cause.** `CreateFramePacer` disagreed with `affinity.ThreadFor(target)` (the write path), or `PostCore` returned `true` after the platform refused the work. A pacer that disagrees with the write path makes each `SamplerSet.Apply` marshal.

**Fix.** Derive the pacer from `affinity.ThreadFor(target)`; return null when the host has no trustworthy wait. `PostCore` must report acceptance honestly — `false` when the queue is gone (`HasShutdownStarted`, a refused `TryEnqueue`, a detached handle). `PostAsync` only waits when the queue said yes. Route by the target, not a captured global.

## The wheel scrolls the page, scrolls twice, or never reaches the canvas

**Symptom.** The platform scroll viewer consumes the notch before the route sees it; or the canvas scrolls and the page scrolls; or a subscriber's `PreventDefault` has no effect.

**Cause.** The adapter handled the wheel in the bubbling phase, let the platform scroll the canvas, or applied the default scroll unconditionally.

**Fix.** Take the non-Ctrl wheel in the tunnel/preview phase (the WPF surface uses `PreviewMouseWheel` on the surface host), gate it to the canvas's own scroll container, route it as `PointerWheelEventArgs`, mark it handled, and apply `input.Scroller?.ScrollBy(0d, e.Delta)` only when `!handle.PreventDefault`. The platform container must never scroll the workflow canvas. Register `Scroller` and `Navigator` on attach and clear only your own on detach.

## A saved graph opens at the origin, or the camera drifts near an edge

**Symptom.** Positions round-trip but the viewport does not; or zoom/pan near an extent edge leaves the point under the pointer drifting a little each notch.

**Cause.** The first `PushViewport` overwrote `Layout.ViewportOffset` before the saved position was captured, or the restore ran before the platform laid out the tree (extent still zero), or the zoom chain re-read the scroll maximum before `ExpandCanvas`.

**Fix.** Call `SurfaceViewportRestoreCore.Capture(tree)` before the first `PushViewport`, then `Schedule(host, afterRestore)`. Defer through `ISurfaceViewportRestoreHost.DeferRestore` until `IsRestoreEnabled`; if the platform is not ready, keep the pending position for the next pass. For zoom, follow the commit chain and clamp the final scroll to the re-read range after `ExpandCanvas` / `UpdateLayout`.

## Links vanish, detach, or truncate at deep zoom

**Symptom.** Everything is correct at 100%; at 40% some links disappear, detach from a port, or clip at a canvas edge.

**Cause.** The renderer received an unbounded absolute coordinate or a whole-world canvas size. Win2D has a texture cap, WinUI needs an offset frame, Jalium culls by layout box, and negative-world content folds past the canvas translate on every zoom-in.

**Fix.** Keep the drawing surface independent of world size: a viewport-sized overlay (MAUI), an offset frame that bakes the pan into geometry (WinUI), or self-bounding per-link geometry (Jalium). Call `WorkflowSurfaceMath.EnsureNegativeCover(tree)` after writing the new `Scale` and before reading `ActualOffset` / `ActualSize`. Test at 40%, not 100%.

## A connection drag never highlights a candidate

**Symptom.** The rubber band follows the pointer, but the candidate port never enters `PreviewReceiver`; releasing over a valid slot does nothing.

**Cause.** The adapter looked for the candidate in the visual tree. During a drag the rubber band covers the surface, so the platform view under the pointer is usually the surface itself. Or hover routing was left on, so every crossed link lights up instead of the candidate.

**Fix.** While `tree.VirtualLink.IsVisible`, move the pointer with `SetPointerCommand` and report `tree.StandardSetPreviewReceiver(SlotHitTestEx.HitTest(tree, x, y))` — the measured anchors are the truth, not the visual tree. Suppress hover routing during the drag. For asynchronous checks, call `StandardRefreshConnectionPreview` when the verdict lands; `PreviewValidating` may only tighten the framework's verdict.

## Delete does nothing while hovering a link

**Symptom.** The link hover works, the context menu works, but pressing Delete has no effect.

**Cause.** The surface has no keyboard focus, or the hovered link's visual is not focusable. On WPF/`UserControl`, `Focusable` defaults to `false`.

**Fix.** Set the host focusable and focus the hovered link's `ILinkHitTestable.Visual` when it is focusable, otherwise the host. Route `KeyDown` with `input.PointerTarget` as the key's `Target`. The adapter only routes the key; deleting is the host's subscription to `KeyDown` running `link.DeleteCommand`.

## The minimap throws, freezes, or updates on the wrong thread

**Symptom.** `InvalidateVisual`-style exceptions from the minimap when nodes move, or a minimap that stops tracking after an unload/reload.

**Cause.** Node/link notifications can arrive from the tick thread through `TreeHelper.BroadcastVisibleItemLayout`; the minimap pipeline is not thread-safe. Or the overlay never resubscribed after leaving and re-entering the visual tree.

**Fix.** Marshal `Invalidate` and the render call to the host thread (`Dispatcher.CheckAccess` / `BeginInvoke`). Set `pipeline.Tree = null` on unload and re-assign it on load so subscriptions are rebuilt; clear the scroll container reference at the same time.

## A pooled view answers for the link it used to show

**Symptom.** Hover or hit-testing hits a link that is no longer at that position, or a deleted link still opens its menu.

**Cause.** The view kept its published curve or its input subscription when the pool rebound it to another item.

**Fix.** On `DataContextChanged`, call `PublishCurve(null)` for the old link, unsubscribe the old `IInputEvents`, then subscribe the new one. Do the same on `Unloaded`. The event route lives as long as the tree; a view lives shorter.

## The adapter grew a workflow view class

**Symptom.** Templates must inherit from an adapter type, or a second platform's template cannot be reused; the adapter package carries `Surface`, `NodeView`, `SlotView`, `LinkLayer`, `GridDecorator` or `Minimap`.

**Cause.** The adapter crossed the layer boundary and shipped workflow controls instead of platform mechanisms.

**Fix.** Delete the view classes. Keep attached behaviors, attachments and platform services; let templates compose the platform's primitives (`Grid`, `Border`, `ScrollViewer`, `Canvas`, `UserControl`) with the attached properties. The adapter may own a surface that must be one object (a single link overlay) but must not expose a subclassable workflow view hierarchy, and must not draw interaction feedback by default.

## Native-island hosts materialize one heavy handle per link

**Symptom.** A WinForms graph with many links allocates one native window per link and slows to a crawl.

**Cause.** The per-link view path was used on a platform where a visual is an HWND.

**Fix.** Implement `IViewLinkLayer.Handles` / `Render` so links are drawn in one pass over the visible set, and keep `workflow-link-view` as an explicit opt-in. Publish the curve from that pass; hit-testing still goes through `LinkHitTestEx`.

## The wheel's other axis snaps back

**Symptom.** A vertical wheel turn moves the canvas but also jumps horizontally to a clamped value.

**Cause.** `ISurfaceScrollHost.ApplyOffset` was implemented with non-nullable values, so the untouched axis was written and clamped.

**Fix.** Honour the nullable contract: pass `null` for an axis the action did not move, and only write the non-null axis. `SurfaceScrollerCore` already passes `null` for a zero delta.
