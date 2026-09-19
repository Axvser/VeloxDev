---
name: veloxdev-create-animation
description: Write VeloxDev interpolation animations — pick the adapter package for the project's GUI, declare transitions in the layout the shipped demos use, choose static-reuse or create-and-discard, express indices (constant, live, frozen), and build an adapter for a GUI that has no official one
---

## Responsibility

Produce animation code that is **readable as prose** and **correct on first run**, for whatever GUI the project already uses. Every rule below matches the layout the shipped demos use — follow it rather than inventing a local style, and read `Examples/Transition/<GUI>/Demo` when a rule is not covered here.

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

This is the shape every declaration takes. The point is that a reader can find the path, the endpoint and the timing without reading the whole statement.

```csharp
private static readonly Transition<Rectangle> Shift =
    Transition<Rectangle>.Create()
        .Property(r => r.Opacity, 0d)
        .Property(r => ((TranslateTransform)r.RenderTransform).X, 40d)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(2000),
            Ease = Eases.Sine.InOut,
        });
```

⚙ `Transition<T>` — `T` is **the object the animation writes into**, and it must be a reference type (`where T : class`). It is not necessarily the control: if the index or the value has to be read from somewhere, a small model object holding both the control's data and that somewhere is the normal shape (see [Indices](#indices)).

⚙ `Transition<T>.Create()` sits on its own line, indented one level from the field.

⚙ **One `.Property(...)` per line**, indented one more level. Never two paths on one line.

⚙ **The opening brace follows the initializer's shape.** For a multi-member initializer the repo writes `new TransitionEffect()` and puts `{` on the **next** line, members one per line, closing with `});` — as above. For a one- or two-member initializer written on a single line, the brace stays on that line: `.Effect(new TransitionEffect { Duration = …, Ease = … })`. What you should not do is start the brace on the same line and then continue for several lines — that form appears nowhere in the repository.

⚙ The declaration ends at `Create()...Effect(...)`. `Execute` is a separate statement, at the place where the animation is actually wanted.

## Static declaration, reused

When the animation has **the same shape and the same endpoint every time it runs**, declare it once and execute it as often as you like:

```csharp
private static readonly Transition<Rectangle> Fade =
    Transition<Rectangle>.Create()
        .Property(r => r.Opacity, 0d)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromSeconds(1),
            Ease = Eases.Sine.InOut,
        });

// …elsewhere
Fade.Execute(rect);
```

⚙ **The declaration is immutable.** Holding it in a `static readonly` field and calling `Execute` many times — including on different targets at once — is the intended use.

⚙ `Execute(target)` is the only step with a side effect, and it returns immediately (the pipeline is `async void`). To know a run finished, read the target's real value, or sequence on it with `AwaitThen`.

⚙ **Mutual by default.** `Execute(target)` supersedes whatever is already running on that target. `Execute(target, CanMutualTask: false)` runs alongside instead.

⚙ **Never cache a declaration that reads a local.** A captured local becomes part of the path's identity and is shared by every later `Execute` — the second call would silently animate the first call's value. Locals belong in the create-and-discard form below.

⚙ `Execute(target, timeline)` anchors several animations to one `ITimeSourceControl` when they must share one transport (pause / rate / seek act on it as a unit). The parameter is positional — it is named `timeline`, not `source`. The default implementation is `TimeSourceCore` (`VeloxDev.Timing`), and `TimerCore.CreateTimeSource<ITimeSourceControl>()` is what resolves it — a platform can register its own there. A `MonoBehaviour` channel's own source comes from `MonoBehaviourManager.Bus(channel)`, which is how an animation shares the frame loop's clock: one `Pause` then stops the frames and the animation together.

## Created on the spot, discarded

When **the endpoint or the path depends on the call**, build it at the call site and let it go:

```csharp
Transition<Border>.Create()
    .Property(b => b.Width, requestedWidth)
    .Effect(new TransitionEffect()
    {
        Duration = TimeSpan.FromMilliseconds(240),
        Ease = Eases.Cubic.Out,
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
| `Items[t.Selected]` — read from the target | **every frame** | follows the target |
| `Items[PathIndex.Frozen(t.Selected)]` | **once per segment** | anchored to the slot it read |

The examples below animate a small model, which is the normal shape when the index has to be readable from the object being animated:

```csharp
private sealed class RampTarget
{
    public LinearGradientBrush Brush { get; set; } = new();
    public int Selected { get; set; }
}
```

```csharp
// constant: the slot is fixed at compile time
.Property(t => t.Brush.GradientStops[0].Color, Colors.Gold)

// follows: change Selected mid-flight and the remaining frames go to the new slot
.Property(t => t.Brush.GradientStops[t.Selected].Color, Colors.Gold)

// anchored: read once when this segment starts, then stays there
.Property(t => t.Brush.GradientStops[PathIndex.Frozen(t.Selected)].Color, Colors.Gold)
```

⚙ **Every frame is the default.** Reach for `PathIndex.Frozen` only when you need the other behaviour — it is the exception, not the safe choice.

⚙ **The deciding question is where the end value must land.** The end value is read once, when the animation starts. With the live form, an index that drifts mid-flight writes an end value computed against the *old* slot into the *new* one. When the end value must land where it was read from, freeze.

⚙ **The index must be readable from the target.** `t.Selected` works because `t` is the object being animated. An index that lives somewhere else has to be reachable from the target — which is why the target is sometimes a small model rather than the control itself.

⚙ `Frozen` binds **once per segment**, so in a multi-segment chain each segment reads the index when that segment starts.

⚙ Arguments are not limited to integers: `Map["player"]`, `Cells[row, column]`, multi-dimensional arrays and custom key types all work the same way.

⚙ `Frozen` may be written anywhere in the chain, but it must stay inside the `.Property(...)` argument — it is recognised by the parser and never executed.

### Index caveats

⚙ **Out of range is silent.** A read that does not resolve skips that property for the frame; a write that does not resolve does nothing. Nothing throws, and nothing is logged — if an animation appears to do nothing, suspect the index before anything else.

⚙ A get-only indexer as the **last** segment cannot be written to, exactly like a read-only property. The same indexer used as a **step** on the way to a writable member is fine.

⚙ An index **whose leaf is declared on a value type** cannot be written — `List<SomeStruct>[0].Width` reads a copy of the struct, so the assignment lands in a temporary. A value type that appears *earlier* on the path is harmless: a reference reached through it still points at the real object. The rule is about the last member, never about how the path got there.

## Effects

```csharp
.Effect(new TransitionEffect()
{
    Duration = TimeSpan.FromSeconds(2),   // how long one segment runs
    IsAutoReverse = true,                 // run back to the start when it reaches the end
    LoopTime = 2,                         // repeat the whole thing; int.MaxValue = forever
    FPS = 60,                             // a maximum sample rate, not a frame grid
    Ease = Eases.Cubic.Out,               // the easing curve
})
```

⚙ Ten curve families ship under `Eases`, each as `In` / `Out` / `InOut` — `Sine`, `Quad`, `Cubic`, `Quart`, `Quint`, `Expo`, `Circ`, `Back`, `Elastic`, `Bounce` — plus `Eases.Default` for a straight line.

⚙ **Easing is not clamped.** `Back` and `Elastic` are meant to leave `[0,1]` and overshoot the end value. What an overshoot *means* is currently per-sampler and **not yet unified**: numeric samplers extrapolate past the endpoint, while the remaining ones pin to it. Do not read a type's overshoot behaviour as a designed contract — unifying the two is an open pass on the library.

⚙ Adapters also ship ready-made effects: `TransitionEffects.Empty` is a zero-duration one for "land here now", and `TransitionEffects.Theme` is the duration the theme switch uses. `TransitionEffects.Hover` is declared too, but nothing in the repository consumes it — treat it as a starting point rather than a convention to follow.

## Segments

⚙ `.Await(ts)` waits before this segment. `.AwaitThen(ts)` waits **after** the previous segment finishes, then starts the next node. `.Then()` moves on with no wait.

⚙ `.Then()` and `.AwaitThen(ts)` return the **next** node, so every `.Property(...)` and `.Effect(...)` written after one of them belongs to that next segment. `.Await(ts)` is the exception: it attaches the wait to the node you are already on and returns that same node.

⚙ Time spent paused inside a wait is not consumed — a pause of any length leaves the remaining delay unchanged.

⚙ **A chain runs every segment, and the whole chain repeats through `Repeat(n)`.** Each segment's loop counts its own passes, so a segment after the first animates rather than breaking out before its first frame; `LoopTime` still repeats *that one segment*, and `Repeat(n)` — written after the last segment — runs the whole chain n further times in all:

```csharp
private static readonly Transition<LinkView> Flow =
    Transition<LinkView>.Create()
        .Property(v => v.Brush.GradientStops[1].Offset, BandFormed)
        .Property(v => v.Brush.GradientStops[1].Color, Lit)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(550),
            Ease = Eases.Default,
        })
        .Then()
        .Property(v => v.Brush.GradientStops[1].Offset, BandLeaving)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(650),
            Ease = Eases.Default,
        })
        .Then()
        .Property(v => v.Brush.GradientStops[1].Offset, BandExit)
        .Property(v => v.Brush.GradientStops[1].Color, Dim)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(550),
            Ease = Eases.Default,
        })
        .Repeat(int.MaxValue);
```

⚙ **Animate the thing you are drawing with, and reach into it with indexed paths.** `T` is the view, the component or the surface that owns the brush — not a carrier object holding a scalar for the setter to map back into geometry. `Brush.GradientStops[1]` is part of the path, so a phase is a handful of writes whose endpoints a reader can check against the constants, and there is no arithmetic in between to get wrong. Introduce a carrier only when there is nothing else to write into: an object whose members the framework does not repaint from, or a value shared by several drawn items (a surface-level band position, say).

⚙ **Every cycle replays the endpoints the first one captured**, which is the same rule a single segment's `LoopTime` follows. A segment therefore starts from the value captured when the chain started, not from wherever the previous cycle left the target. Make the end of a cycle land in the same state as its start and the seam is invisible; leave them different and the value snaps back at every seam, exactly as a looping single segment does.

⚙ `Repeat(0)` — the default — runs the chain once. The count is *additional* cycles, so `Repeat(2)` runs it three times; `int.MaxValue` runs it forever, and `Transition.Exit` stops it between cycles the same way it stops a segment mid-pass.

⚙ **A forever loop needs a pass that takes time.** A pass with `Duration = 0` writes its frame and finishes without yielding, so a loop — segment-level or chain-level — built on zero-duration passes spins instead of looping and cannot be stopped from the thread it is spinning on.

⚙ **A declaration whose endpoints come from the caller is built per caller, not cached.** Two of the phase endpoints above are the link's own colours, so the declaration is built once per view (or per component) and rebuilt when that colour changes — the rule below about locals applies to it: a `static readonly` declaration reading a local shares it with every later execution.

⚙ **When the platform needs a frame hook, attach it with the setter overload.** WPF does not repaint from a gradient-stop write, so a port there ends each segment with `.Effect(e => { e.Duration = …; e.Ease = …; e.LateUpdate += (_, _) => InvalidateVisual(); })` — `Update` runs *before* the frame is applied and `LateUpdate` after, and a replay fires them per cycle like any other pass. An instance handler is safe exactly because the declaration is per view; on a `static readonly` one it would keep the view alive.

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
// per path, in the chain:
.Interpolator((MyNode n) => n.Accent, new MyStepSampler())

// or application-wide:
Interpolator.RegisterInterpolator(typeof(MyColor), new MyColorSampler());
```

⚙ `ISampler` is three members — `NormalizeStart`, `NormalizeEnd`, `InsertFrame` — and implementations must be **stateless**: the registered instance is shared process-wide, and the start/end values handed to `InsertFrame` are shared with the snapshot and must not be mutated.

⚙ `.Interpolator(...)` is an **extension method**, so its generic arguments are inferred from the lambda. Write the lambda's parameter type explicitly (`(MyNode n) => …`); `.Property(...)` does not need this because it is an instance method on `Transition<T>`.

⚙ A **struct** with no registered sampler can still be animated as a whole by implementing `ISampleable`. That interface has two members: `GetAnimatableMembers()` returns the members to interpolate, in the order the second member expects them, and `CreateFrameValue(memberValues)` rebuilds the value from them. Declare the members with `TransitionProperty.ReadableMembers<T>(…)` (or `Members<T>(…)`) and return that from `GetAnimatableMembers()`; `CreateFrameValue` then constructs through the struct's own constructor.

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

⚙ `Examples/Transition/<GUI>/Demo` — one runnable demo per supported GUI, all of them exercising the same API. (Blazor's is nested one level deeper: `Examples/Transition/Blazor/Demo/Demo`.) Two files in most of them are the reference for the parts above:

- **`SamplerSubject.cs`** — the object the animations write into: a real control in the visual tree, one dependency property per sampler, typed exactly like the sampler's product. This is the shape to copy for `Transition<T>`'s `T` when the target is the control itself. Blazor has no copy of this file: the object it animates is a plain `INotifyPropertyChanged` model (`Demo/Demo/Models/BoxModel.cs`), not a visual-tree control.
- **`SamplerProbe.cs`** — one row per sampler, each declaring its endpoints and, where relevant, its path. It holds the two indexed paths (`GradientStops[0].Color` and `[1].Color`) side by side, which is the worked example of "adjacent indices must never overwrite each other", and it is also where the constant-index and live-index forms can be compared directly.

⚙ Both of those are `internal` to the demo project — read them for the shape, do not reference them.

⚙ `Examples/Transition/AUTO TEST/AGENTS.md` — how the acceptance suite is run and extended.
