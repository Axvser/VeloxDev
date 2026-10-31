---
name: veloxdev-tick-loop
description: Give any VeloxDev class a frame loop — mark it with [Tickable], call InitializeTickable(), start the channel with TickManager.Start, and implement the Awake / Start / Update / LateUpdate / FixedUpdate hooks. Use when adding a frame loop or Unity-style update hooks to a plain class.
---

## Responsibility

Put a Unity-style lifecycle on a plain class — `Awake` / `Start` / `Update` / `LateUpdate` / `FixedUpdate` — driven by the loop's own threads or tasks (depending on `UseAsyncLoop`), with no UI framework and no `DispatcherTimer`.

Your class declares the attribute and calls one generated method; the Roslyn analyzer **`VeloxDev.Core.Generator`** writes the `ITickable` implementation and the hook forwarders, and you write the hooks as `partial void`.

Package: **`VeloxDev.Core`**, namespace `VeloxDev.TimeLine`. The class is `partial`. Nothing here touches a GUI: a tick loop behaves identically on every platform. A repository `ProjectReference` does not carry analyzers, so a project using `[Tickable]` also references **`VeloxDev.Core.Generator`** — as a package, or as a `ProjectReference` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`.

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
// Program.cs — top-level statements precede any type declaration in the same file
using VeloxDev.TimeLine;

var physics = new PhysicsComponent();
physics.InitializeTickable();   // generated: SetTargetFPS(60, "physics") + RegisterBehaviour
TickManager.Start("physics");
```

⚙ **The attribute and the call together start the loop.** `InitializeTickable()` registers the behaviour; `TickManager.Start(channel)` starts the channel. Registration is a queue push: the update pump drains it before its next frame is sampled, so `Awake` → `Start` run there, always before the first `Update`, whether you registered before or after `Start`.

⚙ **Call `InitializeTickable()` on a constructed instance, after its constructor.** The drain runs on another thread, so a call from the constructor can run `Awake` before construction finishes.

## Where to read next

| You are about to… | Read |
|---|---|
| Write the hooks and understand their order | [references/practices/lifecycle.md](references/practices/lifecycle.md) |
| Name a channel, share a clock, or run work on the pump | [references/practices/channels-and-manager.md](references/practices/channels-and-manager.md) |
| Start, stop, pause, retime or query a channel | [references/practices/channels-and-manager.md](references/practices/channels-and-manager.md#the-manager-surface) |

## Pitfalls

| Symptom | Read |
|---|---|
| The attribute is applied but no hook runs | [references/pitfalls/tick.md](references/pitfalls/tick.md) |
| A hook with a body does not compile | [references/pitfalls/tick.md](references/pitfalls/tick.md) |
| `CloseTickable()` + `InitializeTickable()` leaves the behaviour off | [references/pitfalls/tick.md](references/pitfalls/tick.md) |
| The loop keeps running but the window stops updating | [references/pitfalls/tick.md](references/pitfalls/tick.md) |
| A zero time scale freezes the channel and `Resume` does not help | [references/pitfalls/tick.md](references/pitfalls/tick.md) |

## Reference

⚙ `Examples/Tickable/WPF/Demo` — the working sample. The window itself is the tickable (`[Tickable(DemoChannel.Name)]` on `MainWindow`, in `MainWindow.Hooks.cs`; `DemoChannel.Name` is `TickManager.DEFAULT_CHANNEL`), registered in `Loaded`. Its hooks integrate a ball through `Update` and `FixedUpdate` so the two pumps can be compared, and the window half only polls, formats and draws. Close calls `CloseTickable()` and then a deliberately **un-awaited** `StopAsync` — the pumps are background threads or tasks depending on `UseAsyncLoop`, and the stop waits up to 1000 ms, so awaiting would only make the close look stuck.

⚙ `Src/Core/VeloxDev.Core/TimeLine/` — `TickableAttribute`, `TickManager`, `FrameEventArgs` and its `TimeLineEventArgs` base; `TickChannelEventArgs` is declared in the same file as `TickManager`.

⚙ `Src/Core/VeloxDev.Core/Interfaces/Tickable/ITickable.cs` — `InitializeTickable`, `CloseTickable` and the five `Invoke…` forwarders.

⚙ `Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs` — the emitted shape and the file name `{Class}_{NamespaceSegment}_Tick.g.cs` (namespace dots become underscores, the global namespace becomes `Global`), which is why two same-named classes in different namespaces do not collide.

⚙ The one consumer inside the library is `TreeHelper<T>` (`[Tickable(channel: nameof(TreeHelper), fps: 10)]`), which runs the workflow canvas's virtualization on its own channel. It starts `nameof(TreeHelper)` if that channel is not already running, registers only when virtualization is enabled, and calls `CloseTickable()` on uninstall.

⚙ Tests: `dotnet test Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj --filter "FullyQualifiedName~TimeLine"` covers the tick bus, the manager, the event args and the attribute; `TimelineControlTests` and the theme timeline test match the same filter.
