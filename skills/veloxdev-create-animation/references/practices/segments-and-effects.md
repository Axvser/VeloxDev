# Segments and effects

## Effects

```csharp
.Effect(new TransitionEffect()
{
    Duration = TimeSpan.FromSeconds(2),   // how long one segment runs
    IsAutoReverse = true,                 // run back to the start when it reaches the end
    LoopTime = 2,                         // repeat this segment's pass; int.MaxValue = forever
    FPS = 60,                             // a maximum sample rate, not a frame grid
    Ease = Eases.Cubic.Out,               // the easing curve
})
```

⚙ Ten curve families ship under `Eases`, each as `In` / `Out` / `InOut` — `Sine`, `Quad`, `Cubic`, `Quart`, `Quint`, `Expo`, `Circ`, `Back`, `Elastic`, `Bounce` — plus `Eases.Default` for a straight line.

⚙ **Easing is not clamped.** `Back` and `Elastic` are meant to leave `[0,1]` and overshoot the end value. What an overshoot *means* is per-sampler: numeric samplers extrapolate past the endpoint, while the remaining ones pin to it.

⚙ Adapters also ship ready-made effects: `TransitionEffects.Empty` is a zero-duration one for "land here now", `TransitionEffects.Theme` is the duration the theme switch uses, and `TransitionEffects.Hover` is a starting point; no shipped flow consumes it.

⚙ **The event args carry position, not an outcome triple.** The seven lifecycle events (`Awaked`, `Start`, `Update`, `LateUpdate`, `Canceled`, `Completed`, `Finally`) are `EventHandler<TransitionEventArgs>`. `TransitionEventArgs` has `Loop` (`int`, the pass of the running segment, `0` on its first) and `Cycle` (`long`, the target's pass count), plus `Handled` / `DeltaTime` / `TotalTime` inherited from `TimeLineEventArgs`; setting `Handled = true` ends the run.

⚙ The two diagnostic events are typed: `Warn` is `EventHandler<TransitionEventArgs<WarnStage, string>>` and `Error` is `EventHandler<TransitionEventArgs<ErrorStage, Exception>>`; each argument exposes `Stage` and `Value`. `WarnStage` is `Unreadable` / `Unsampled` / `Dropped`; `ErrorStage` names the failed step — `Sampling`, `Run`, `Marshaling`, `Awake`, `Prepare`, then the lifecycle event when a subscriber threw. Deduplication is per diagnostics instance.

## Segments

```csharp
Transition<View>.Create()
    .Property(v => v.Opacity, 1d)
    .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(200) })
    .AwaitThen(TimeSpan.FromMilliseconds(100))   // wait, then start the next segment
    .Property(v => v.X, 40d)
    .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(300) })
    .Then()
    .Property(v => v.Scale, 1.2d)
    .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(150) })
    .Repeat(int.MaxValue)
    .Execute(view);
```

⚙ `.Await(ts)` waits before this segment. `.AwaitThen(ts)` waits **after** the previous segment finishes, then starts the next node. `.Then()` moves on with no wait.

⚙ `.Then()` and `.AwaitThen(ts)` return the **next** node, so every `.Property(...)` and `.Effect(...)` written after one of them belongs to that next segment. `.Await(ts)` attaches the wait to the node you are already on and returns that same node.

⚙ Time spent paused inside a wait is not consumed — a pause of any length leaves the remaining delay unchanged.

⚙ **A chain runs every segment, and `Repeat(n)` loops it.** `Repeat` configures the segment it is written after, and that segment's loop wraps the chain *from its first segment through that one*, n further iterations — a count on the last segment repeats the whole chain, and counts on several segments nest by where they end:

```csharp
// The view's two phase scalars: how far the light has travelled, and how lit it is.
private const double BandFormed = 0.30;
private const double BandLeaving = 0.78;

private static readonly Transition<PolylineCurveView> Flow =
    Transition<PolylineCurveView>.Create()
        // phase one: leave the sender, lighting up as it goes
        .Property(v => v.BandHead, BandFormed)
        .Property(v => v.BandIntensity, 1d)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(450),
            Ease = Eases.Default,
        })
        .Then()
        // phase two: fully lit, travelling
        .Property(v => v.BandHead, BandLeaving)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(700),
            Ease = Eases.Default,
        })
        .Then()
        // phase three: arrive and go dark. Both ends are the "no light" state, so the loop seam is invisible.
        .Property(v => v.BandHead, 1d)
        .Property(v => v.BandIntensity, 0d)
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromMilliseconds(450),
            Ease = Eases.Default,
        })
        .Repeat(int.MaxValue);
```

⚙ **Every iteration after a segment's first replays the endpoints that first iteration captured**, the same rule a single segment's `LoopTime` follows. Make the end of a loop land in the same state as its start and the seam is invisible; leave them different and the value snaps back at every seam.

⚙ Three segments each carrying `Repeat(1)` run `1, 1, 2, 1, 1, 2, 3, 1, 1, 2, 1, 1, 2, 3` — the middle segment's loop closes around the first, and the last's closes around both, so the innermost repetition is the earliest segment's. A count on the *first* segment is the narrow one (`1, 1, 1, 2, 3` for `Repeat(2)`), and only the last segment's count means the whole chain.

⚙ `Repeat(0)` — the default — runs that segment's loop once. The count is *additional* iterations, so `Repeat(2)` runs the chain three times; `int.MaxValue` runs it forever, and `Transition.Exit` stops it between iterations the same way it stops a segment mid-pass. A forever loop on a segment other than the last means the segments after it never run.

⚙ **A forever loop needs a pass that takes time.** A pass with `Duration = 0` writes its frame and finishes without yielding, so a loop built on zero-duration passes spins instead of looping and cannot be stopped from the thread it is spinning on.

## Frame hooks

⚙ **When the platform needs a frame hook, attach it with the setter overload.** A renderer that keeps retained drawing — WPF, or Blazor's component tree — sees the change when told, so such a view can end each segment with:

```csharp
.Effect(e =>
{
    e.Duration = TimeSpan.FromMilliseconds(200);
    e.Ease = Eases.Sine.InOut;
    e.LateUpdate += (_, _) => InvalidateVisual();          // Blazor: InvokeAsync(StateHasChanged)
})
```

`Update` runs *before* the frame is applied and `LateUpdate` after, and a replay fires them per cycle like any other pass. An instance handler is safe because the declaration is per view; a `static readonly` declaration would keep the view alive.

⚙ **Prefer the platform's own change notification where one exists.** A WPF dependency property registered with `AffectsRender`, or a `PropertyMetadata` callback into `InvalidateVisual`, needs no per-frame handler.

⚙ **A declaration whose endpoints come from the caller is built per caller, not cached.** The Flow declarations that read only constants can be `static readonly`; a view whose phase endpoints come from its own colours builds the declaration once per view and rebuilds it when that colour changes.
