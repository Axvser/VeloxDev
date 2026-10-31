# Pitfalls — flow and travelling highlights

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [flow.md](../practices/flow.md).

## A graph with many links drops frames

**Symptom:** frame time and GC pressure scale with the number of links.

**Cause:** one `Transition` per link view, each with its own frame pacer and phase machine. The motion does not depend on the link, so the cost multiplied for no visual gain.

**Fix:** own the motion once per tree with `WorkflowFlowClock`, or once per surface on an immediate-mode canvas. Views subscribe to the shared scalar; only one timer runs per tree.

## The band does not move although the animation runs

**Symptom:** the transition reports frames, but the link looks static.

**Cause:** the path writes into a brush collection (`GradientStops[i].Offset` / `.Color`) on a retained-mode framework, which changes the value without repainting. The same route also measured as an identical frame on Jalium.

**Fix:** animate a scalar on the view/clock and derive geometry and colour from it in the draw pass; trigger the platform's repaint (a dependency/styled property with `AffectsRender`, `InvalidateVisual`, or `StateHasChanged`).

## A Blazor link never shows a flow

**Symptom:** the Blazor canvas draws links, but no travelling highlight.

**Cause:** the per-frame work was looked for in a per-link `Transition` or a `Phase` property. The Blazor link is drawn by `flow.js`, which owns a single `requestAnimationFrame` loop for every link; the `Phase` property in the demo belongs to the **slot** view's ripple.

**Fix:** register the link with `flow.js` when its geometry or style changes; the JS does the frames. Do not add a per-link `Transition` to the Blazor link.

## A recycled link animates the previous link

**Symptom:** a pooled link shows the old band position or hover.

**Cause:** the view was rebound without unsubscribing from the old clock or resetting its scalars.

**Fix:** unsubscribe on detach/rebind, reset `BandHead` / `BandIntensity` (or `FlowPhase`), and subscribe to the new link's clock.

## The band stutters while the canvas pans or zooms

**Symptom:** the highlight jumps or freezes during a camera move.

**Cause:** two writers — the flow cycle and a link-move handler — both write the band's position.

**Fix:** the cycle owns the phase; the move handler re-aims the gradient and re-colours the shoulders without writing the phase.

## A selected link and the moving band fight

**Symptom:** a selected link shows a moving band instead of a flat highlight.

**Cause:** the flow runs on every settled link, including the selected one.

**Fix:** keep a flat pen for the virtual link and for the selected link; run the band only on settled, unselected links.

## The loop seam is visible

**Symptom:** the band jumps at the end of each cycle.

**Cause:** the last phase does not return to the start state (dark, head at the start).

**Fix:** make the first and last phases share the same "no light" state; the Flow's three phases do this.
