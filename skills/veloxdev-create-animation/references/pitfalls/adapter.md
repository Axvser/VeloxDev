# Pitfalls — platform adapters

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [adapter.md](../practices/adapter.md).

## Every animation runs at two frames and stops

**Symptom:** values freeze at the start; no exception; the closed-form half of the test suite still passes.

**Cause:** the frame timer is non-repeating. `Arm` is called once per frame and `Disarm` stops the timer on every tick, so the pacer expects each tick to re-arm. MAUI's `IDispatcherTimer` fires once when `IsRepeating = false`; WinUI's `DispatcherQueueTimer` re-arms correctly.

**Fix:** create the timer with the repeating behavior the host needs (`IsRepeating = true` on MAUI) and verify one animation end to end.

## The animation hangs forever

**Symptom:** after the first frame, the pipeline never completes; the UI may still respond.

**Cause:** `PostCore` returned `true` for work the queue silently dropped. `PostAsync` waits on a completion source once the queue reported acceptance; an optimistic `true` for dropped work leaves the consumer waiting for the rest of the process.

**Fix:** return `false` when the queue cannot accept the action (`HasShutdownStarted`, a refused `TryEnqueue`, a detached handle). Razor is the documented exception; it relies on cancellation and the host's `Dispose`.

## Every frame is a dispatch

**Symptom:** animation cost is much higher than expected; samplers run on the UI thread for every property.

**Cause:** the frame pacer resolves on a different thread than the write path.

**Fix:** derive the pacer from `affinity.ThreadFor(target)`, never from the platform. Return null from `CreateFramePacer` when the target is not current, so each write marshals itself.

## The bottom two corners swap on one platform

**Symptom:** a corner radius animates to the wrong corners on WinUI or MAUI.

**Cause:** the constructors take the corners in different orders — MAUI `(topLeft, topRight, bottomLeft, bottomRight)`, WinUI `(topLeft, topRight, bottomRight, bottomLeft)`.

**Fix:** copy the constructor call from the platform you are writing for; do not port the argument order.

## A theme switch snaps instead of animating

**Symptom:** `ThemeManager.Transition` completes instantly, with no error.

**Cause:** `CreateScheduler` is missing, returns null for the platform's effect, or the host never called `ThemeManager.SetPlatformInterpolator(new Interpolator())` at startup.

**Fix:** implement `CreateScheduler` with `FindOrCreate`, test the effect against the platform's `ITransitionEffect<TPriority>`, and call `SetPlatformInterpolator` before any theme object registers.

## A pause/seek/exit cannot find a running animation

**Symptom:** the control-plane call does nothing.

**Cause:** the scheduler was constructed with `new` instead of `FindOrCreate`, so it was never filed under the target.

**Fix:** always go through `TransitionSchedulerCore<…>.FindOrCreate(target)`.

## A stale frame overwrites a reset

**Symptom:** after `Exit` or a reset, one more frame lands and restores the old value.

**Cause:** cancellation was checked before marshaling but not after the write landed on the UI thread.

**Fix:** re-check cancellation after marshaling. `SamplerSet` does this; a custom frame path must too.

## A sampler writes into a temporary

**Symptom:** a nested path is never updated, or the value updates in a copy.

**Cause:** the sampler returned the interpolated scratch at `t == 0` / `t == 1`, replacing the runtime type the nested path depends on.

**Fix:** return the caller's own `start` / `end` at those exact times.

## A custom subclass is written back unmutated

**Symptom:** a transform/brush subclass does not animate.

**Cause:** the clone fast path has no case for the subclass; WPF falls back to `Freezable.Clone()` and `MutateInPlace` has no case, so the scratch is written back unchanged.

**Fix:** gate the clone fast path on the exact known types (`IsKnownTransform`) and exclude unknown subclasses.

## The adapter test coverage fails after adding a sampler

**Symptom:** a coverage test fails; a published sampler is unaccounted for.

**Cause:** every sampler needs a table row or an unreachable-list entry.

**Fix:** add the row with its declared type and closed form, or the unreachable entry with the endpoint value that cannot be built headlessly.
