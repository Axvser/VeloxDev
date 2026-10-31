# Channels and the manager

## Channels

A **channel** is the loop itself, named by a string. The attribute's `channel` defaults to `TickManager.DEFAULT_CHANNEL` (`"default"`), and `fps` to `-1`; any `fps < 1` emits no `SetTargetFPS` call and leaves the channel's existing rate alone (a new channel is at 60). An `fps >= 1` makes the generated `InitializeTickable()` call `TickManager.SetTargetFPS(fps, channel)` before registering.

⚙ **`fps` is a channel-wide target, not a per-behaviour rate.** Classes sharing a channel that declare different values overwrite each other in registration order.

⚙ Several classes may share one channel, and the channel is paused, re-timed, restarted or stopped as a unit. A class that wants a loop of its own gets a channel of its own — `[Tickable(channel: nameof(TreeHelper))]` is the idiom.

⚙ **A channel's clock is the one to share when several things must move together.** `TickManager.Bus(channel)` returns the channel's `VeloxDev.Timing.ITimeSourceControl`, or `null` if the channel was never created. Pass it to a transition snapshot's `Execute(target, timeline)` overload and the animation runs on the loop's clock, so one `Pause` stops the frames and the animation together. The bus exposes `IsAdvancing`, `IsPaused`, `Rate`, `Position`, `Ticks`, `Epoch`, `Pause()`, `Resume()`, `SetRate(double)`, `Seek(TimeSpan)` and `Wake()` (`Seek` rebases and bumps `Epoch`; `Wake` nudges a parked consumer once).

⚙ `TickManager.ExecuteOnMainThread(action, channel)` runs the delegate on the **update pump's** thread at the start of its next iteration — not on a UI thread. A GUI host still marshals to the dispatcher itself.

## The manager surface

`TickManager` (static) is the whole public face. Every member takes an optional trailing `channel` defaulting to `DEFAULT_CHANNEL`; `ChannelNames` and `UseAsyncLoop` take none.

| Group | Members |
|---|---|
| Run | `Start`, `StopAsync`, `RestartAsync`, `Pause`, `Resume`, `TogglePause` |
| Membership | `RegisterBehaviour`, `UnregisterBehaviour` |
| Rate and clock | `SetTargetFPS`, `SetFixedUpdateInterval`, `SetTimeScale`, `SetUseAsyncLoop`, `ClearUseAsyncLoopOverride`, `UseAsyncLoop` (global property) |
| Queries | `IsRunning`, `IsPaused`, `CurrentFPS`, `TargetFPS`, `TotalTime`, `TotalTimeMs`, `TotalFrames`, `ActiveBehaviorCount`, `TimeScale`, `SystemStatus`, `IsUpdateThreadAlive`, `IsFixedUpdateThreadAlive`, `ChannelNames`, `Bus` |
| Marshalling and events | `ExecuteOnMainThread`, `OnChannelStarted` / `OnChannelPaused` / `OnChannelResumed` / `OnChannelStopped` |

The four events are static and process-wide, carry `TickChannelEventArgs` with a single `ChannelName`, and fire only on a real transition — a redundant `Start`, or `StopAsync` on a non-running channel, raises nothing. Filter by `ChannelName`; the sender is the private channel object, not `TickManager`.

## Timing

⚙ **`SetTimeScale` is the channel's playback rate, and it is verbatim:** it moves `DeltaTime` and `TotalTime` together, a negative value throws `ArgumentOutOfRangeException`, and `0` freezes the clock — no frames arrive at all rather than frames carrying a zero delta. `IsPaused` stays `false`, `SystemStatus` still says `"Running"`, and `Resume()` does not lift it; only a non-zero rate does. `StopAsync` does not clear a `0` rate either.

⚙ **`SetTargetFPS` and `SetFixedUpdateInterval` accept `1..1000`** and silently ignore anything outside it.

⚙ **`SetUseAsyncLoop` and `ClearUseAsyncLoopOverride` throw `InvalidOperationException` while the channel is running** and take effect at the next `Start`.

⚙ **`Start` is idempotent.** `CurrentFPS` is measured off the wall clock and republished about once per second, so a short run can read `0`. A paused channel parks its pumps, but both liveness queries still return `true`.

⚙ **`StopAsync` clears the four queues but not the applied registrations.** It resets statistics and drains the add, remove, config and main-thread queues, but leaves the applied behaviours alone: a behaviour whose registration was already drained ticks again after the next `Start`, without re-running `Awake` / `Start`. Only a registration or unregistration still queued at the stop is discarded. Re-registering the instance — the generated `CloseTickable()` / `InitializeTickable()` pair, or `RegisterBehaviour` directly — re-runs `Awake` / `Start` on a fresh wrapper; `RestartAsync` and stop/start do not. `StopAsync` waits for the pumps for at most 1000 ms and then clears the queues and statistics regardless, so `OnChannelStopped` means it stopped waiting — not that every hook has returned.

## What a hook can do

⚙ **Anything a hook throws is swallowed** to `Debug.WriteLine` (and is silent in Release). A hook that throws contributes nothing and reports nothing; a loop that silently stops drawing is the failure mode to suspect first.

⚙ **A hook runs on a channel pump — a thread or an async task, depending on `UseAsyncLoop` — so it does not touch the UI.** `Update` / `LateUpdate` run on one pump and `FixedUpdate` on another, concurrently; any field both touch needs its own synchronisation. A `Dispatcher.Invoke` from a hook is exactly the case the swallow hides: the loop keeps running, the window stops updating, and nothing is logged. Publish a value from the hook and let the view poll it — that is what the demo does.

⚙ **Performance:** registration is queued, and the add/remove drain rebuilds a sorted dispatch snapshot. The snapshot enumerates a `ConcurrentDictionary`, whose order is unspecified, so the insertion sort is `O(N²)` average and worst case; the rebuild runs on a membership change or at most once per second, not per frame.
