# Controlling a run and extending the samplers

## Controlling a running animation

All of it is addressed by **target**, and all of it is static on `Transition`:

```csharp
Transition.Pause(target, IncludeMutual: true, IncludeNoMutual: true);
Transition.Resume(target, IncludeMutual: true, IncludeNoMutual: true);
Transition.SetRate(target, 0.25d, IncludeMutual: true, IncludeNoMutual: true);
Transition.Seek(target, TimeSpan.FromMilliseconds(300), IncludeMutual: true, IncludeNoMutual: true);
Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

var at = Transition.Position(target);
var pass = Transition.Cycle(target);
var paused = Transition.IsPaused(target);
var rate = Transition.Rate(target);
```

⚙ `Exit` **freezes in place** — it does not jump to the end. Writing the declared start back is a separate step, and it is the caller's job.

⚙ `IncludeMutual` / `IncludeNoMutual` select which animations the call reaches: mutual ones (started with the default `Execute`) and concurrent ones (started with `CanMutualTask: false`). Pass both unless you have a reason not to. The defaults are `IncludeMutual: true`, `IncludeNoMutual: false`, so a bare `Transition.Pause(target)` leaves concurrent runs alone.

⚙ The schedulers under a target are also readable directly (`using VeloxDev.TransitionSystem.Abstractions;`): `TransitionSchedulerCore.TryGetMutualScheduler(target, out var mutual)` and `TryGetNoMutualScheduler(target, out var schedulers)` exist, and `RemoveNoMutualScheduler(target)` stops and removes the concurrent ones. Read the returned array's **length** to count live concurrent runs — the table entry itself outlives them. There is no `RemoveMutualScheduler`: the mutual scheduler is cached for the target's lifetime.

⚙ Pause keeps the position frozen and costs no timer wake-ups; rate changes take effect without a jump; time only moves forwards; a negative rate is rejected.

⚙ `Seek` re-anchors the run to a pass and a position; seeking past the end of a pass finishes it at the endpoint. `Cycle` / `Position` read the first run on the target. These are per-target, while `Pause` / `Resume` / `SetRate` move a shared timeline when several runs are anchored to it.

## Custom samplers

To interpolate a type the adapter does not cover, implement `ISampler` and attach it to one path — or register it for the whole application.

```csharp
// per path, in the chain:
.Interpolator((MyNode n) => n.Accent, new MyStepSampler())

// or application-wide:
Interpolator.RegisterInterpolator(typeof(MyColor), new MyColorSampler());
```

⚙ `ISampler` is three members — `NormalizeStart`, `NormalizeEnd`, `InsertFrame` — and implementations are **stateless**: the registered instance is shared process-wide, and the start/end values handed to `InsertFrame` are shared with the snapshot and must not be mutated.

⚙ `.Interpolator(...)` is an **extension method**, so its generic arguments are inferred from the lambda. Write the lambda's parameter type explicitly (`(MyNode n) => …`); `.Property(...)` does not need this because it is an instance method on `Transition<T>`.

⚙ `NormalizeStart` / `NormalizeEnd` run **once per segment**; `InsertFrame` runs **once per frame**. Put anything expensive in the normalizers.

⚙ **When the product feeds a nested path, `t == 0` and `t == 1` hand back the caller's own instance.** A path such as `((TranslateTransform)x.RenderTransform).X` depends on the runtime type the endpoint was declared with; returning a scratch object would replace it. The transform samplers early-return `start` / `end` at exactly those times; Core makes the pass's final frame exact.

⚙ **A struct with no registered sampler is animated as a whole by implementing `ISampleable`.** `GetAnimatableMembers()` returns the members to interpolate, in the order `CreateFrameValue(memberValues)` expects them. Declare them with `TransitionProperty.ReadableMembers<T>(…)` and return that; `CreateFrameValue` constructs through the struct's own constructor. Use `ReadableMembers`, not `Members<T>`: `Members<T>` keeps only readable **and writable** members, so on a readonly struct it returns an empty list and the value is skipped.

⚙ **A registration covers the type you declare.** The lookup walks exact type, then base classes nearest-first, then interfaces. Register the type users will declare; a registration for a concrete subclass leaves a property declared as the base unserved. A more general registration must handle the whole family — a `Brush` sampler receives gradients too.

⚙ **Register a custom sampler before the switch or animation that needs it.** `InterpolatorCore` exposes its registry through `TryGetInterpolator` / `RegisterInterpolator` / `UnregisterInterpolator`; there is no public registry property.

## How a platform adapter enters

A UI framework gets animation support by implementing `UIThreadInspector`, the samplers for its value types, and an `Interpolator` that registers them and implements `CreateScheduler`. Nothing in Core knows about any UI framework. Full contract, per class, including the traps that differ between the seven shipped adapters: [adapter.md](adapter.md).

⚙ Start from the shipped adapter whose host most resembles yours (`Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`).

⚙ `CreateScheduler` is also what makes a theme switch animate on that platform: the switch runs on the transition system, and this is the one thing Core cannot answer for it. A missing `SetPlatformInterpolator(new Interpolator())` at startup means Core never reaches `CreateScheduler`, and the switch happens instantly, with nothing logged.

## Pitfalls

When a run stops early, a loop cannot be stopped, or a custom type never animates, read [references/pitfalls/segments.md](../pitfalls/segments.md) and [references/pitfalls/samplers.md](../pitfalls/samplers.md).
