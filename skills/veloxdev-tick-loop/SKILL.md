---
name: veloxdev-tick-loop
description: Give any VeloxDev class a frame loop — mark it with [Tickable], call InitializeTickable(), start the channel with TickManager.Start, and implement the Awake / Start / Update / LateUpdate / FixedUpdate hooks
---

## Responsibility

Put a Unity-style lifecycle on a plain class — `Awake` / `Start` / `Update` / `LateUpdate` / `FixedUpdate` — running on the loop's own background threads, with no UI framework involved and no `DispatcherTimer` beside it.

Your class declares the attribute and calls one generated method; the generator writes the `ITickable` implementation and the hook plumbing, and you write the hooks as `partial void`.

Package: **`VeloxDev.Core`**, namespace `VeloxDev.TimeLine`. Nothing here touches a GUI, which is why it stands on its own rather than living in the view-model layer: a tick loop behaves identically on every platform.

## The declaration

```csharp
[Tickable(channel: "physics", fps: 60)]
public partial class PhysicsComponent
{
    public PhysicsComponent() => InitializeTickable();

    partial void Awake() { }
    partial void Start() { }
    partial void Update(FrameEventArgs e) { … }
    partial void LateUpdate(FrameEventArgs e) { … }
    partial void FixedUpdate(FrameEventArgs e) { … }
}

TickManager.Start("physics");
```

⚙ **The attribute alone does nothing.** You must call `InitializeTickable()` (conventionally from the constructor) **and** start the channel with `TickManager.Start(channel)`. A class that never ticks is almost always a missing `Start`.

⚙ **The hook name and signature must match exactly**, or the hook is simply never invoked — there is no warning. `Update`, `LateUpdate` and `FixedUpdate` take `FrameEventArgs`; `Awake` and `Start` take nothing.

⚙ **`[Tickable]` has to be the real attribute.** The generator matches the full name `VeloxDev.TimeLine.TickableAttribute`; an attribute of your own with the same short name is ignored without a word.

⚙ `CloseTickable()` is generated too, and unregisters the instance from its channel. It pairs with `InitializeTickable()`; a long-lived class that should stop ticking needs it.

## The five hooks

| Hook | When it runs |
|---|---|
| `Awake` | Once, as the instance is registered. |
| `Start` | Once, when the first frame reaches the instance. |
| `Update(FrameEventArgs e)` | Once per frame, on the channel's update thread, before `LateUpdate`. `DeltaTime` is measured off the clock and never compensated. |
| `LateUpdate(FrameEventArgs e)` | Once per frame, after every `Update`. |
| `FixedUpdate(FrameEventArgs e)` | On the channel's fixed-update thread, as many times per wake-up as the owed constant step requires. `DeltaTime` is the step size, not the wall-clock delta. |

`FrameEventArgs` carries `DeltaTime`, `TotalTime`, `CurrentFPS` and `TargetFPS`. Setting `e.Handled = true` stops the remaining instances for that frame — `Handled` lives on the base `TimeLineEventArgs`.

## Channels

A **channel** is the loop itself, named by a string. The attribute's `channel` defaults to `TickManager.DEFAULT_CHANNEL` (`"default"`), and `fps` to `-1`, which means "leave the channel's existing rate alone" (a channel starts at 60).

Several classes may share one channel, and the channel is paused, re-timed, restarted or stopped as a unit. A class that wants a loop of its own gets a channel of its own — `[Tickable(channel: nameof(TreeHelper))]` is the idiom for that.

⚙ **A channel's clock is the one to share when several things must move together.** `TickManager.Bus(channel)` returns its `ITimeSourceControl`; hand that to `Transition.Execute` and an animation runs on the loop's clock, so one `Pause` stops the frames and the animation together.

## The manager surface

`TickManager` (static) is the whole public face:

| | |
|---|---|
| Run | `Start`, `StopAsync`, `RestartAsync`, `Pause`, `Resume`, `TogglePause` — all taking a channel |
| Membership | `RegisterBehaviour`, `UnregisterBehaviour` |
| Rate and clock | `SetTargetFPS`, `SetFixedUpdateInterval`, `SetTimeScale`, `SetUseAsyncLoop`, `ClearUseAsyncLoopOverride` |
| Queries | `IsRunning`, `IsPaused`, `CurrentFPS`, `TargetFPS`, `ActiveBehaviorCount`, `SystemStatus`, `IsUpdateThreadAlive`, `IsFixedUpdateThreadAlive`, `ChannelNames`, `Bus` |
| Marshalling and events | `ExecuteOnMainThread`, `OnChannelStarted` / `OnChannelPaused` / `OnChannelResumed` / `OnChannelStopped` |

⚙ `SetTimeScale` is the channel's playback **rate**, and it is verbatim: it moves `DeltaTime` and `TotalTime` together, a negative value throws, and `0` freezes the clock — no frames arrive at all rather than frames carrying a zero delta.

⚙ **Anything a hook throws is swallowed** to `Debug.WriteLine`. A hook that throws contributes nothing and reports nothing, so a loop that silently stops drawing is the failure mode to suspect first.

⚙ **A hook runs on a channel thread, so it must not touch the UI.** A `Dispatcher.Invoke` from inside a hook is exactly the case the swallow hides: the loop keeps running, the window stops updating, and nothing is logged. Publish a value from the hook and let the view poll it — that is what the demo does.

## Reference

⚙ `Examples/Tickable/WPF/Demo` — the working sample. The window itself is the tickable (`[Tickable(DemoChannel.Name)]` on `MainWindow`, in `MainWindow.Hooks.cs`); its two hooks integrate a ball each through `Update` and `FixedUpdate` so the two pumps can be compared, and the window half only polls, formats and draws. Close calls `CloseTickable()` and then a deliberately **un-awaited** `StopAsync` — the pumps are background threads, so awaiting would only make the close look stuck.

⚙ `Src/Core/VeloxDev.Core/TimeLine/` — `TickableAttribute`, `TickManager`, `FrameEventArgs` (and its `TimeLineEventArgs` base, which carries `Handled` plus the shared `DeltaTime`/`TotalTime`).

⚙ `Src/Core/VeloxDev.Core/Interfaces/Tickable/ITickable.cs` — what the generator implements for you. You only implement it by hand if you are replacing the generator, and then you own all seven members: `InitializeTickable`, `CloseTickable` and the five `Invoke…` forwarders.

⚙ `Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs` — the emitted shape, including the file name (`{类}_{命名空间}_Tick.g.cs`, which is why two same-named classes in different namespaces do not collide).

⚙ The one consumer inside the library is `TreeHelper<T>` (`[Tickable(channel: nameof(TreeHelper), fps: 10)]`), which runs the workflow canvas's virtualization on its own channel. That is the pattern to copy when a graph needs a periodic view concern: the helper owns the hook, the host owns start and stop.
