# Pitfalls — tick loops

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [lifecycle.md](../practices/lifecycle.md) and [channels-and-manager.md](../practices/channels-and-manager.md).

## The attribute is applied but no hook runs

**Symptom:** `[Tickable]` is on the class; nothing ticks.

**Cause:** `InitializeTickable()` was not called, the channel was not started, or a same-named attribute from another namespace was used.

**Fix:** call `InitializeTickable()` on a constructed instance and `TickManager.Start(channel)`. Use `VeloxDev.TimeLine.TickableAttribute`; the generator matches the full name.

## A hook with a body does not compile

**Symptom:** `CS0759` (no defining declaration) or `CS0111` (duplicate member).

**Cause:** the hook name or signature does not match the generated `partial void`, or the method is not `partial`.

**Fix:** match the signatures exactly — `Awake()` / `Start()` take nothing; `Update` / `LateUpdate` / `FixedUpdate` take `FrameEventArgs`. Write them as `partial void`.

## `InitializeTickable()` does not exist

**Symptom:** `CS0103`, or a `CS1061` on a generated member.

**Cause:** the analyzer is missing. A `ProjectReference` does not carry analyzers.

**Fix:** reference `VeloxDev.Core.Generator` as a package, or as a `ProjectReference` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`.

## `Awake` runs before the constructor finishes

**Symptom:** a field is null in `Awake`, or state is half-initialized.

**Cause:** `InitializeTickable()` was called from the constructor. The drain runs on another thread.

**Fix:** call it after construction, from `Loaded` / `OnStart` / host initialization.

## The pair leaves the behaviour off the channel

**Symptom:** after `CloseTickable(); InitializeTickable();`, `Awake`/`Start` ran but no `Update` arrives.

**Cause:** one drain applies additions before removals, so the new registration is removed in the same drain.

**Fix:** separate the two calls by at least one pump drain — the demo uses two button clicks.

## The loop keeps running but the window stops updating

**Symptom:** hooks continue; the UI freezes on the last frame; nothing is logged.

**Cause:** a hook called `Dispatcher.Invoke` (or touched the UI). Hooks run on a channel pump, not the UI thread; the exception is swallowed to `Debug.WriteLine`.

**Fix:** publish a value from the hook and let the view poll or repaint through its own dispatcher. Do not marshal UI work from inside the hook.

## A zero time scale freezes the channel and `Resume` does not help

**Symptom:** no frames arrive; `IsPaused` is false; `SystemStatus` says `"Running"`.

**Cause:** `SetTimeScale(0)` freezes the clock without pausing. `Resume()` does not lift a zero rate.

**Fix:** call `SetTimeScale` with a non-zero value. `StopAsync` does not clear a `0` rate either.

## `Update` and `FixedUpdate` race on a field

**Symptom:** intermittent corruption or torn values.

**Cause:** the two hooks run on different pumps, concurrently.

**Fix:** synchronise the shared field, or keep each pump's state separate and merge on the update thread.

## A short run reports `0` FPS

**Symptom:** `CurrentFPS` reads `0` right after start.

**Cause:** FPS is measured off the wall clock and republished about once per second.

**Fix:** read it after a second of running, or use `TotalFrames` / `TotalTime` for a short run.

## `OnChannelStopped` fires while hooks are still running

**Symptom:** the host believes every hook has returned; a late callback touches disposed state.

**Cause:** `StopAsync` waits for the pumps for at most 1000 ms, then clears queues and statistics regardless.

**Fix:** treat `OnChannelStopped` as "the stop stopped waiting". Make hooks observe cancellation and avoid touching disposed state after a stop.

## A restart unexpectedly re-runs `Awake`

**Symptom:** `Awake` / `Start` run again after the channel was restarted.

**Cause:** the instance was re-registered — the generated `CloseTickable()` / `InitializeTickable()` pair, or `RegisterBehaviour` directly — rather than restarted. `RestartAsync` and stop/start keep the applied behaviours and do not re-run the start hooks; a fresh registration creates a fresh wrapper and runs them.

**Fix:** choose one lifecycle. Use `RestartAsync` / stop-start to keep the behaviours, or a re-registration to create a fresh wrapper.
