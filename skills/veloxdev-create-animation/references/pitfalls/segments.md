# Pitfalls — segments and loops

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [segments-and-effects.md](../practices/segments-and-effects.md).

## A forever loop hangs a core and cannot be stopped

**Symptom:** an animation with `Repeat(int.MaxValue)` or `LoopTime = int.MaxValue` spins at 100% CPU and `Transition.Exit` on the same thread does not stop it.

**Cause:** the looping pass has `Duration = 0`. A zero-duration pass writes its frame and finishes without yielding, so the loop never reaches an await and cannot observe cancellation.

**Fix:** give every looping pass a non-zero duration. A "land here now" effect belongs in a non-looping chain.

## A loop snaps back at every seam

**Symptom:** the animation visibly jumps when a repeat begins.

**Cause:** the end state differs from the start state. Every iteration after a segment's first replays the endpoints that first iteration captured, so the next iteration starts from the captured start, not from where the previous iteration ended.

**Fix:** make the loop's end land in the same state as its start (the Flow's three phases do this: both ends are the "no light" state).

## A `Repeat` count on a middle segment does not repeat the whole chain

**Symptom:** the chain repeats fewer or more times than expected.

**Cause:** a segment's `Repeat(n)` wraps the chain from its first segment through *that* segment, not the whole chain. Counts on several segments nest.

**Fix:** put the whole-chain count on the last segment. Three segments each carrying `Repeat(1)` run `1, 1, 2, 1, 1, 2, 3, 1, 1, 2, 1, 1, 2, 3`; only the last segment's count spans everything.

## Segments after a forever loop never run

**Symptom:** a chain with `Repeat(int.MaxValue)` on an early segment stops before the later segments.

**Cause:** a forever loop on a segment other than the last never reaches the segments after it. That is what the nesting means.

**Fix:** move the forever loop to the last segment, or split the chain.

## A wait runs before the wrong segment

**Symptom:** a delay appears before the previous animation finishes, or the next segment starts immediately.

**Cause:** `.Await(ts)` attaches the wait to the node you are already on; `.AwaitThen(ts)` waits after the previous segment and returns the next node. Mixing the two puts the wait in the wrong place.

**Fix:** use `.AwaitThen(ts)` to delay the *next* segment; use `.Await(ts)` to delay the current one. Every `.Property` / `.Effect` after `.Then()` / `.AwaitThen()` belongs to the next segment.

## A `static readonly` declaration keeps a view alive

**Symptom:** a view is never collected, or a frame handler fires after the view is gone.

**Cause:** a frame hook (`LateUpdate += (_, _) => InvalidateVisual()`) was attached to a declaration cached for the application. The handler holds the view.

**Fix:** attach instance frame handlers only to a declaration built per view, or use the platform's own change notification (a dependency property with `AffectsRender`) so no handler is needed.

## A frame hook fires at the wrong time

**Symptom:** the drawn frame lags one frame, or a replay does not repaint.

**Cause:** the hook was attached to `Update` (before the frame is applied) instead of `LateUpdate` (after), or the retained-mode view has no repaint trigger.

**Fix:** use `LateUpdate` for a post-write repaint; `Update` for pre-write work. A replay fires both per cycle.

## The easing overshoots a value that cannot overshoot

**Symptom:** a size or a unit kind goes negative or invalid with `Back` / `Elastic`.

**Cause:** easing is not clamped, and the sampler extrapolates or constructs an invalid value.

**Fix:** use a clamped sampler or a non-overshooting ease for values with a hard range. Sizes share a zero-floored progress; discrete values switch at a threshold.
