---
name: veloxdev-tick-loop
description: Give any VeloxDev class a frame loop — mark it with [Tickable], call InitializeTickable(), start the channel with TickManager.Start, and implement the Awake / Start / Update / LateUpdate / FixedUpdate hooks. Use when adding a frame loop or Unity-style update hooks to a plain class.
---

## Responsibility

Put a Unity-style lifecycle on a plain class — `Awake` / `Start` / `Update` / `LateUpdate` / `FixedUpdate` — driven by the loop's own threads or tasks (depending on `UseAsyncLoop`), with no UI framework and no `DispatcherTimer`.

Your class declares the attribute and calls one generated method; the Roslyn analyzer **`VeloxDev.Core.Generator`** writes the `ITickable` implementation and the hook forwarders, and you write the hooks as `partial void`.

Package: **`VeloxDev.Core`**, namespace `VeloxDev.TimeLine`. The class must be declared `partial`. Nothing here touches a GUI, which is why it stands on its own rather than living in the view-model layer: a tick loop behaves identically on every platform. A repository `ProjectReference` does not carry analyzers, so a project using `[Tickable]` must also reference **`VeloxDev.Core.Generator`** — as a package, or as a `ProjectReference` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"` (see `Examples/Tickable/WPF/Demo/Demo.csproj`).

## The declaration

```csharp
// PhysicsComponent.cs
using VeloxDev.TimeLine;

[Tickable("physics", TargetFPS = 60)]
public partial class PhysicsComponent
{
    partial void Awake() { }
    partial void Start() { }
    partial void Update(FrameEventArgs e) { /* integrate */ }
    partial void LateUpdate(FrameEventArgs e) { }
    partial void FixedUpdate(FrameEventArgs e) { /* step */ }
}
```

```csharp
// Program.cs — top-level statements must precede any type declaration in the same file
using VeloxDev.TimeLine;

var physics = new PhysicsComponent();
physics.InitializeTickable();   // generated: SetTargetFPS(60, "physics") + RegisterBehaviour
TickManager.Start("physics");
```

⚙ **The attribute alone does nothing.** You must call `InitializeTickable()` on a constructed instance **and** start the channel with `TickManager.Start(channel)`. Registration is a queue push: the update pump drains it before its next frame is sampled, so `Awake` → `Start` run there, always before the first `Update`, whether you registered before or after `Start`. Do not call `InitializeTickable()` from the constructor — the drain runs on another thread, so `Awake` can run before construction finishes.

⚙ **The hook name and signature must match exactly**, or the build fails rather than skipping the hook: the generator emits the defining `partial void` declarations, so a hook with a body but no matching declaration is `CS0759`, and a non-partial method with the exact signature collides with the generated one (`CS0111`). Only a plain method the generator never forwards is silent. `Update`, `LateUpdate` and `FixedUpdate` take `FrameEventArgs`; `Awake` and `Start` take nothing.

⚙ **`[Tickable]` has to be the real attribute.** The generator matches the full name `VeloxDev.TimeLine.TickableAttribute`; an attribute of your own with the same short name is ignored without a word. It applies to classes only, is not inherited, and allows one instance per class. `TickableAttribute.Channel` is get-only, so a named `Channel = ...` argument is `CS0617` and the channel can only be passed positionally; `TargetFPS` is settable, so `[Tickable("physics", TargetFPS = 60)]` is the form to use. The generator reads named arguments after positional ones, and a named value wins.

⚙ **A missing analyzer is not `CS0535`.** Without the generator your class never declares `ITickable`; every implemented `partial void` hook reports `CS0759` (no defining declaration), and the remaining failures are `CS0103` on `InitializeTickable` (or `CS1061` on a generated member) — not a missing-interface error. To inspect the output, build with `-p:EmitCompilerGeneratedFiles=true`; it lands under `obj/<Configuration>/<tfm>/generated/VeloxDev.Core.Generator/VeloxDev.Generators.Tickable/`.

⚙ `CloseTickable()` is generated too, and enqueues an unregistration. It pairs with `InitializeTickable()`; removal is silent (there is no close hook). Registering the same instance again runs `Awake` / `Start` again on a fresh wrapper. **Do not call the pair back-to-back**: one drain applies additions before removals, so `CloseTickable(); InitializeTickable();` registers and then immediately unregisters — `Awake` / `Start` run, but the behaviour ends up off the channel. Separate the two calls by at least one pump drain; the demo splits them across two button clicks (`Examples/Tickable/WPF/Demo/MainWindow.xaml.cs`).

## The five hooks

| Hook | When it runs |
|---|---|
| `Awake` | Once per applied registration, on the update thread in the registration drain, before the first frame. |
| `Start` | Immediately after `Awake` in the same drain, before the first `Update`. |
| `Update(FrameEventArgs e)` | Once per frame on the update thread, before `LateUpdate`. `DeltaTime` is measured and uncompensated. |
| `LateUpdate(FrameEventArgs e)` | Once per frame, after every behaviour's `Update`; shares that frame's arguments and `Handled` flag. |
| `FixedUpdate(FrameEventArgs e)` | On the fixed-update thread, concurrently with `Update` / `LateUpdate`, once per owed constant step; a stall is repaid as a batch. |

`FrameEventArgs` carries `DeltaTime`, `TotalTime`, `CurrentFPS` and `TargetFPS`; the two times and `Handled` live on the base `TimeLineEventArgs`. All setters except `Handled` are `internal`, so a hook reads the measurements and writes only `Handled`.

⚙ **`e.Handled = true` stops the rest of the `Update` sweep and the whole `LateUpdate` sweep for that frame.** The behaviour that set it is not skipped; everything after it is. `FixedUpdate` steps build their own `FrameEventArgs`, so they are unaffected.

⚙ **The arguments object is pooled and reused for every hook of a frame.** Do not store `e` beyond the hook call.

⚙ **Fixed-step delivery is independent of the frame rate.** With time scale 1, `FixedUpdate` is called about `(1000/h)·N` times over `N` seconds for a step of `h` ms — about 62.5/s at the default 16 ms — regardless of the target FPS `F`. One wake-up may deliver several owed steps; the catch-up burst is capped at 8 steps per advance, and debt above 64 steps is forgiven rather than repaid.

## Channels

A **channel** is the loop itself, named by a string. The attribute's `channel` defaults to `TickManager.DEFAULT_CHANNEL` (`"default"`), and `fps` to `-1`; any `fps < 1` emits no `SetTargetFPS` call and leaves the channel's existing rate alone (a new channel is at 60). An `fps >= 1` makes the generated `InitializeTickable()` call `TickManager.SetTargetFPS(fps, channel)` before registering. `fps` is a channel-wide target, not a per-behaviour rate: classes sharing a channel that declare different values overwrite each other in registration order.

Several classes may share one channel, and the channel is paused, re-timed, restarted or stopped as a unit. A class that wants a loop of its own gets a channel of its own — `[Tickable(channel: nameof(TreeHelper))]` is the idiom for that.

⚙ **A channel's clock is the one to share when several things must move together.** `TickManager.Bus(channel)` returns the channel's `VeloxDev.Timing.ITimeSourceControl`, or `null` if the channel was never created. Pass it to a transition snapshot's `Execute(target, timeline)` overload and the animation runs on the loop's clock, so one `Pause` stops the frames and the animation together. The bus exposes `IsAdvancing`, `IsPaused`, `Rate`, `Position`, `Ticks`, `Epoch`, `Pause()`, `Resume()`, `SetRate(double)`, `Seek(TimeSpan)` and `Wake()` (`Seek` rebases and bumps `Epoch`; `Wake` nudges a parked consumer once).

⚙ `TickManager.ExecuteOnMainThread(action, channel)` runs the delegate on the **update pump's** thread at the start of its next iteration — not on a UI thread. A GUI host still has to marshal to the dispatcher itself.

## The manager surface

`TickManager` (static) is the whole public face. Every member takes an optional trailing `channel` defaulting to `DEFAULT_CHANNEL`; `ChannelNames` and `UseAsyncLoop` take none.

| | |
|---|---|
| Run | `Start`, `StopAsync`, `RestartAsync`, `Pause`, `Resume`, `TogglePause` |
| Membership | `RegisterBehaviour`, `UnregisterBehaviour` |
| Rate and clock | `SetTargetFPS`, `SetFixedUpdateInterval`, `SetTimeScale`, `SetUseAsyncLoop`, `ClearUseAsyncLoopOverride`, `UseAsyncLoop` (global property) |
| Queries | `IsRunning`, `IsPaused`, `CurrentFPS`, `TargetFPS`, `TotalTime`, `TotalTimeMs`, `TotalFrames`, `ActiveBehaviorCount`, `TimeScale`, `SystemStatus`, `IsUpdateThreadAlive`, `IsFixedUpdateThreadAlive`, `ChannelNames`, `Bus` |
| Marshalling and events | `ExecuteOnMainThread`, `OnChannelStarted` / `OnChannelPaused` / `OnChannelResumed` / `OnChannelStopped` |

The four events are static and process-wide, carry `TickChannelEventArgs` with a single `ChannelName`, and fire only on a real transition — a redundant `Start`, or `StopAsync` on a non-running channel, raises nothing. Filter by `ChannelName`; the sender is the private channel object, not `TickManager`.

⚙ `SetTimeScale` is the channel's playback **rate**, and it is verbatim: it moves `DeltaTime` and `TotalTime` together, a negative value throws `ArgumentOutOfRangeException`, and `0` freezes the clock — no frames arrive at all rather than frames carrying a zero delta. `IsPaused` stays `false`, `SystemStatus` still says `"Running"`, and `Resume()` will not lift it; only a non-zero rate does. `StopAsync` does not clear a `0` rate either.

⚙ `SetTargetFPS` and `SetFixedUpdateInterval` accept `1..1000` and silently ignore anything outside it. `SetUseAsyncLoop` and `ClearUseAsyncLoopOverride` throw `InvalidOperationException` while the channel is running and take effect at the next `Start`. `Start` is idempotent. `CurrentFPS` is measured off the wall clock and republished about once per second, so a short run can read `0`. A paused channel parks its pumps, but both liveness queries still return `true`.

⚙ **`StopAsync` clears the four queues but not the applied registrations.** It resets statistics and drains the add, remove, config and main-thread queues, but leaves `_behaviors` alone: a behaviour whose registration was already drained ticks again after the next `Start`, without re-running `Awake` / `Start`. Only a registration or unregistration still queued at the stop is discarded. Re-registering the instance — the generated `CloseTickable()` / `InitializeTickable()` pair, or `RegisterBehaviour` directly — re-runs `Awake` / `Start` on a fresh wrapper; `RestartAsync` and stop/start do not. The pair must be separated by at least one pump drain (see above), or the unregistration cancels the new registration in the same drain. `StopAsync` waits for the pumps for at most 1000 ms and then clears the queues and statistics regardless, so `OnChannelStopped` means it stopped waiting — not that every hook has returned.

⚙ **Anything a hook throws is swallowed** to `Debug.WriteLine` (and is silent in Release). A hook that throws contributes nothing and reports nothing, so a loop that silently stops drawing is the failure mode to suspect first.

⚙ **A hook runs on a channel pump — a thread or an async task, depending on `UseAsyncLoop` — so it must not touch the UI.** `Update` / `LateUpdate` run on one pump and `FixedUpdate` on another, concurrently; any field both touch needs its own synchronisation. A `Dispatcher.Invoke` from a hook is exactly the case the swallow hides: the loop keeps running, the window stops updating, and nothing is logged. Publish a value from the hook and let the view poll it — that is what the demo does.

## Reference

⚙ `Examples/Tickable/WPF/Demo` — the working sample. The window itself is the tickable (`[Tickable(DemoChannel.Name)]` on `MainWindow`, in `MainWindow.Hooks.cs`; `DemoChannel.Name` is `TickManager.DEFAULT_CHANNEL`), registered in `Loaded`. Its hooks integrate a ball each through `Update` and `FixedUpdate` so the two pumps can be compared, and the window half only polls, formats and draws. Close calls `CloseTickable()` and then a deliberately **un-awaited** `StopAsync` — the pumps are background threads or tasks depending on `UseAsyncLoop`, and the stop waits up to 1000 ms, so awaiting would only make the close look stuck.

⚙ `Src/Core/VeloxDev.Core/TimeLine/` — `TickableAttribute`, `TickManager`, `FrameEventArgs` and its `TimeLineEventArgs` base; `TickChannelEventArgs` is declared in the same file as `TickManager`.

⚙ `Src/Core/VeloxDev.Core/Interfaces/Tickable/ITickable.cs` — what the generator implements for you: `InitializeTickable`, `CloseTickable` and the five `Invoke…` forwarders. You only implement it by hand if you are replacing the generator.

⚙ `Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs` — the emitted shape and the file name `{Class}_{NamespaceSegment}_Tick.g.cs` (namespace dots become underscores, the global namespace becomes `Global`), which is why two same-named classes in different namespaces do not collide.

⚙ The one consumer inside the library is `TreeHelper<T>` (`[Tickable(channel: nameof(TreeHelper), fps: 10)]`), which runs the workflow canvas's virtualization on its own channel. It starts `nameof(TreeHelper)` if that channel is not already running, registers only when virtualization is enabled, and calls `CloseTickable()` on uninstall. That is the pattern to copy when a graph needs a periodic view concern.

⚙ Performance: registration is queued, and the add/remove drain rebuilds a sorted dispatch snapshot. The snapshot enumerates a `ConcurrentDictionary`, whose order is unspecified, so the insertion sort is `O(N²)` average and worst case; the rebuild runs on a membership change or at most once per second, not per frame.

⚙ Tests: `dotnet test Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj --filter "FullyQualifiedName~TimeLine"` → **43 pass** — `TickableBusTests` 12, `TickManagerTests` 11, `TimeLineEventArgsTests` 3, `TickableAttributeTests` 2, plus 15 more matching the filter: 14 in `TransitionSystem/TimelineControlTests.cs` and `DynamicTheme/ThemeTransitionTests.cs:158`.
