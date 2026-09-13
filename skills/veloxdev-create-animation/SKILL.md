---
name: veloxdev-create-animation
description: Write VeloxDev interpolation animations — pick the adapter package for the project's GUI, declare transitions in the canonical readable layout, choose static-reuse or create-and-discard, express indices (constant, live, frozen), and build an adapter for a GUI that has no official one
---

## Responsibility

Produce animation code that is **readable as prose** and **correct on first run**, for whatever GUI the project already uses. Every rule below is a convention this repository already follows — follow them rather than inventing a local style.

## Package selection

Identify the project's GUI, then reference **one** package. Versions are deliberately not stated: take the latest published.

| GUI | Package |
|---|---|
| WPF | `VeloxDev.WPF` |
| Avalonia | `VeloxDev.Avalonia` |
| WinUI 3 / Windows App SDK | `VeloxDev.WinUI` |
| .NET MAUI | `VeloxDev.MAUI` |
| Windows Forms | `VeloxDev.WinForms` |
| Blazor / Razor | `VeloxDev.Razor` |
| Jalium | `VeloxDev.Jalium` |

The adapter package brings `VeloxDev.Core` in transitively, so that one reference is enough. Add `VeloxDev.Core` explicitly only if you also want its non-UI parts.

If the project's GUI is **not** in that table, stop and read [references/adapter.md](references/adapter.md) before writing any animation code.

One `using` covers everything: `using VeloxDev.TransitionSystem;` — every adapter exposes the same namespaces, so the same animation code compiles on all of them.

## The canonical layout

This is the shape every declaration takes. It is not a suggestion — the point is that a reader can find the path, the endpoint and the timing without reading the whole statement.

```csharp
private static readonly Transition<SamplerSubject> Shift =
    Transition<SamplerSubject>.Create()
        .Property(s => s.Fill, CreateShiftedBrush())
        .Effect(new TransitionEffect {
            Duration = TimeSpan.FromMilliseconds(2000),
            Ease = Eases.Sine.InOut
        });
```

⚙ `Transition<T>` — `T` is **the object the animation writes into**, and it must be a reference type (`where T : class`). It is not necessarily the control: if the index or the value has to be read from somewhere, a small model object holding both the control's data and that somewhere is the normal shape.

⚙ `Transition<T>.Create()` sits on its own line, indented one level from the field.

⚙ **One `.Property(...)` per line**, indented one more level. Never two paths on one line, and never wrap a single path across lines.

⚙ `.Effect(new TransitionEffect {` — the opening brace stays on the **same line** as `new TransitionEffect`. Initializer members go one per line, indented again; the statement closes with `});`.

⚙ The declaration ends at `Create()...Effect(...)`. `Execute` is a separate statement, at the place where the animation is actually wanted.

## Static declaration, reused

When the animation has **the same shape and the same endpoint every time it runs**, declare it once and execute it as often as you like:

```csharp
private static readonly Transition<Rectangle> Fade =
    Transition<Rectangle>.Create()
        .Property(r => r.Opacity, 0d)
        .Effect(new TransitionEffect {
            Duration = TimeSpan.FromSeconds(1),
            Ease = Eases.Sine.InOut
        });

// …elsewhere
Fade.Execute(rect);
```

⚙ **The declaration is immutable.** Holding it in a `static readonly` field and calling `Execute` many times — including on different targets at once — is the intended use.

⚙ `Execute(target)` is the only step with a side effect, and it returns immediately (the pipeline is `async void`). To know a run finished, read the target's real value, or sequence on it with `AwaitThen`.

⚙ **Mutual by default.** `Execute(target)` supersedes whatever is already running on that target. `Execute(target, CanMutualTask: false)` runs alongside instead.

⚙ **Never cache a declaration that reads a local.** A captured local becomes part of the path's identity and is shared by every later `Execute` — the second call would silently animate the first call's value. Locals belong in the create-and-discard form below.

⚙ `Execute(target, source)` anchors several animations to one `ITimeSourceControl` when they must share one transport (pause / rate / seek act on it as a unit). The default implementation is `TimeSourceCore` (`VeloxDev.Timing`), and `TimerCore.CreateTimeSource<ITimeSourceControl>()` is what resolves it — a platform can register its own there. A `MonoBehaviour` channel's own source comes from `MonoBehaviourManager.Bus(channel)`, which is how an animation shares the frame loop's clock: one `Pause` then stops the frames and the animation together.

## Created on the spot, discarded

When **the endpoint or the path depends on the call**, build it at the call site and let it go:

```csharp
Transition<Border>.Create()
    .Property(b => b.Width, requestedWidth)
    .Effect(new TransitionEffect {
        Duration = TimeSpan.FromMilliseconds(240),
        Ease = Eases.Cubic.Out
    })
    .Execute(border);
```

⚙ Creating a declaration allocates a handful of objects. When the endpoint varies per call, building it on the spot is cheaper than caching and *correct*, where caching is not.

⚙ The rule of thumb: **same shape and same endpoint → `static readonly`; endpoint computed per call → create and discard.**

## Indices

An index in a path is not decoration — **which slot** is part of the path's identity, so `Items[0].Opacity` and `Items[1].Opacity` are two different paths that never overwrite each other.

| Written as | Evaluated | Behaviour |
|---|---|---|
| `Items[0]` | compile time | pinned to slot 0 |
| `Items[i]` — `i` captured | **every frame** | follows `i` |
| `Items[x.SelectedIndex]` — read from the target | **every frame** | follows the target |
| `Items[PathIndex.Frozen(x.SelectedIndex)]` | **once per segment** | anchored to the slot it read |

```csharp
// constant: the slot is fixed at compile time
.Property(s => ((LinearGradientBrush)s.Ramp!).GradientStops[0].Color, Colors.Gold)

// follows: change Selected mid-flight and the remaining frames go to the new slot
.Property(s => ((LinearGradientBrush)s.Ramp!).GradientStops[s.Selected].Color, Colors.Gold)

// anchored: read once when this segment starts, then stays there
.Property(s => ((LinearGradientBrush)s.Ramp!).GradientStops[PathIndex.Frozen(s.Selected)].Color, Colors.Gold)
```

⚙ **Every frame is the default.** Reach for `PathIndex.Frozen` only when you need the other behaviour — it is the exception, not the safe choice.

⚙ **The deciding question is where the end value must land.** The end value is read once, when the animation starts. With the live form, an index that drifts mid-flight writes an end value computed against the *old* slot into the *new* one. When the end value must land where it was read from, freeze.

⚙ **The index must be readable from the target.** `p.Selected` works because `p` is the object being animated. An index that lives somewhere else has to be reachable from the target — which is why the target is sometimes a small model rather than the control itself.

⚙ `Frozen` binds **once per segment**, so in a multi-segment chain each segment reads the index when that segment starts.

⚙ Arguments are not limited to integers: `Map["player"]`, `Cells[row, column]`, multi-dimensional arrays and custom key types all work the same way.

⚙ `Frozen` may be written anywhere in the chain, but it must stay inside the `.Property(...)` argument — it is recognised by the parser and never executed.

### Index caveats

⚙ **Out of range is silent.** A read that does not resolve skips that property for the frame; a write that does not resolve does nothing. Nothing throws, and nothing is logged — if an animation appears to do nothing, suspect the index before anything else.

⚙ A get-only indexer as the **last** segment cannot be written to, exactly like a read-only property. The same indexer used as a **step** on the way to a writable member is fine.

⚙ An index reached through a **value-type** intermediate (`x.SomeStruct[0].Width`) cannot be written: the value read through the struct is a copy, so the write is lost. Reach the member through a reference instead.

## Effects

```csharp
.Effect(new TransitionEffect {
    Duration = TimeSpan.FromSeconds(2),   // how long one segment runs
    IsAutoReverse = true,                 // run back to the start when it reaches the end
    LoopTime = 2,                         // repeat the whole thing; int.MaxValue = forever
    FPS = 60,                             // a maximum sample rate, not a frame grid
    Ease = Eases.Cubic.Out                // the easing curve
})
```

⚙ Ten curve families ship under `Eases`, each as `In` / `Out` / `InOut` — `Sine`, `Quad`, `Cubic`, `Quart`, `Quint`, `Expo`, `Circ`, `Back`, `Elastic`, `Bounce` — plus `Eases.Default` for a straight line.

⚙ **Easing is not clamped.** `Back` and `Elastic` are meant to leave `[0,1]` and overshoot the end value; the samplers decide what an overshoot means for their type.

⚙ Adapters also ship ready-made effects: `TransitionEffects.Theme` and `.Hover` are the durations those two paths use, and `.Empty` is a zero-duration one for "land here now".

## Segments

⚙ `.Await(ts)` waits before this segment. `.AwaitThen(ts)` waits **after** the previous segment finishes, then starts the next node. `.Then()` moves on with no wait.

⚙ These return the **next node**: every `.Property(...)` and `.Effect(...)` written after one of them belongs to that next segment, not to the one before.

⚙ Time spent paused inside a wait is not consumed — a pause of any length leaves the remaining delay unchanged.

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
```

⚙ `Exit` **freezes in place** — it does not jump to the end. Writing the declared start back is a separate step, and it is the caller's job.

⚙ `IncludeMutual` / `IncludeNoMutual` select which animations the call reaches: mutual ones (started with the default `Execute`) and concurrent ones (started with `CanMutualTask: false`). Pass both unless you have a reason not to.

⚙ Pause keeps the position frozen and costs no timer wake-ups; rate changes take effect without a jump; time only moves forwards (there is no reverse playback, and a negative rate is rejected rather than clamped).

## Custom samplers

To interpolate a type the adapter does not cover, implement `ISampler` and attach it to one path — or register it for the whole application.

```csharp
.Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1), Ease = Eases.Sine.InOut })
```

```csharp
// per path, in the chain:
.Interpolator((SamplerSubject s) => s.Fill, new StepSampler())

// or application-wide:
Interpolator.RegisterInterpolator(typeof(MyColor), new MyColorSampler());
```

⚙ `ISampler` is three members — `NormalizeStart`, `NormalizeEnd`, `InsertFrame` — and implementations must be **stateless**: the registered instance is shared process-wide, and the start/end values handed to `InsertFrame` are shared with the snapshot and must not be mutated.

⚙ `.Interpolator(...)` is an **extension method**, so its generic arguments are inferred from the lambda. Write the lambda's parameter type explicitly (`(SamplerSubject s) => …`); `.Property(...)` does not need this because it is an instance method on `Transition<T>`.

⚙ A **struct** with no registered sampler can still be animated as a whole by implementing `ISampleable`: declare its members with `TransitionProperty.ReadableMembers<T>(…)` and rebuild the value in `CreateFrameValue`.

## Building an adapter

A UI framework gets support by implementing a handful of small classes against Core's interfaces. Nothing in Core knows about any UI framework — which is why supporting an unsupported GUI is a contained job rather than a fork, and why an adapter is also what makes a **theme switch** animate on that platform.

Full contract, per class, including the traps that differ between the seven shipped adapters: [references/adapter.md](references/adapter.md).

⚙ Start from the shipped adapter whose host most resembles yours (`Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`), rather than from the interfaces.

## Pitfalls

⚙ **Do not block.** The pipeline never blocks a thread, and an adapter must not either — no `Thread.Sleep`, no spin waits. Precision is not the goal; each pass is anchored to an absolute timeline position, so a wake-up that lands early or late still draws the right frame.

⚙ **One target, one animation, unless you mean it.** Starting a second mutual animation on the same target cancels the first. When several things must animate at once, give each its own target — a whole batch sharing one object looks like "the earlier ones did nothing".

⚙ **Do not assume `Execute` has finished** when it returns. It never has.

⚙ **Do not rebuild a brush or a transform per frame** to animate part of it. Address the part you mean with an index path; the framework redraws on its own.

## Reference

⚙ `Examples/Transition/<GUI>/Demo` — one runnable demo per supported GUI, all of them exercising the same API. Two files in each are the reference for the parts above:

- **`SamplerSubject.cs`** — the object the animations write into: a real control in the visual tree, one dependency property per sampler, typed exactly like the sampler's product. This is the shape to copy for `Transition<T>`'s `T`.
- **`SamplerProbe.cs`** — one row per sampler, each declaring its endpoints and, where relevant, its path. It holds the two indexed paths (`Ramp.GradientStops[0].Color` and `[1].Color`) side by side, which is the worked example of "adjacent indices must never overwrite each other", and it is also where the constant-index and live-index forms can be compared directly.

⚙ `Examples/Transition/AUTO TEST/AGENTS.md` — how the acceptance suite is run and extended.
