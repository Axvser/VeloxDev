# Declarations and paths

## The target object

`Transition<T>` writes into `T`, and `T` is a reference type (`where T : class`). It is the object whose properties the paths name. When the path needs an index or value that lives elsewhere, make `T` a small model that holds both the control and that value:

```csharp
private sealed class RampTarget
{
    public LinearGradientBrush Brush { get; set; } = new();
    public int Selected { get; set; }
}
```

## Paths

```csharp
Property(t => t.Brush.GradientStops[0].Color, Colors.Gold)
Property(t => ((TranslateTransform)t.RenderTransform).X, 40d)
Property(t => t.Items[i].Width, 120d)
```

⚙ **A path is a member chain rooted at the target.** The compiler reads it when the declaration is built; the write goes through the same path on every frame.

⚙ **An index in a path is part of the path's identity.** `Items[0].Opacity` and `Items[1].Opacity` are two different paths that never overwrite each other.

## Indices

| Written as | Evaluated | Behaviour |
|---|---|---|
| `Items[0]` | compile time | pinned to slot 0 |
| `Items[i]` — `i` captured | **every frame** | follows `i` |
| `Items[t.Selected]` — read from the target | **every frame** | follows the target |
| `Items[PathIndex.Frozen(t.Selected)]` | **once per segment** | anchored to the slot it read |

⚙ **Every frame is the default.** Reach for `PathIndex.Frozen` when the end value must land where it was read from.

⚙ **The deciding question is where the end value must land.** Each segment prepares its own endpoints: the end value is read once, when that segment starts. With the live form, an index that drifts mid-flight writes an end value computed against the *old* slot into the *new* one. When the end value must land where it was read from, freeze.

⚙ **The index is readable from the target.** `t.Selected` works because `t` is the object being animated. An index that lives somewhere else is reachable from the target — which is why the target is sometimes a small model.

⚙ `Frozen` binds **once per segment**, so in a multi-segment chain each segment reads the index when that segment starts.

⚙ Arguments are not limited to integers: `Map["player"]`, `Cells[row, column]`, multi-dimensional arrays and custom key types all work the same way.

⚙ `Frozen` may be written anywhere in the chain, and it stays inside the `.Property(...)` argument — the parser recognises it and never executes it.

### Index caveats

⚙ **Out of range is silent at frame time.** The compiled accessor maps `IndexOutOfRangeException` / `ArgumentOutOfRangeException` / `KeyNotFoundException` to a miss: a write that does not resolve does nothing, and a read that does not resolve skips the property. Nothing throws. A miss seen while `Prepare` reads the start value is reported once through the effect's `Warn` as `WarnStage.Unreadable`; per-frame misses are not reported. When an animation appears to do nothing, check the index first.

⚙ A get-only indexer as the **last** segment cannot be written to, exactly like a read-only property. The same indexer used as a **step** on the way to a writable member is fine.

⚙ An index **whose leaf is declared on a value type** cannot be written — `List<SomeStruct>[0].Width` reads a copy of the struct, so the assignment lands in a temporary. A value type that appears *earlier* on the path is harmless: a reference reached through it still points at the real object. The rule is about the last member.

## Static reuse and per-call construction

```csharp
// Same shape and endpoint every time: declare once.
private static readonly Transition<Rectangle> Fade = …;

Fade.Execute(rect);
```

```csharp
// Endpoint or path from the call: build at the call site.
Transition<Border>.Create()
    .Property(b => b.Width, requestedWidth)
    .Effect(new TransitionEffect()
    {
        Duration = TimeSpan.FromMilliseconds(240),
        Ease = Eases.Cubic.Out,
    })
    .Execute(border);
```

⚙ **The rule of thumb: same shape and same endpoint → `static readonly`; endpoint computed per call → create and discard.** Building a declaration allocates a handful of objects; when the endpoint varies per call, building on the spot is both cheaper and correct.

⚙ **A declaration is immutable.** Holding it in a `static readonly` field and calling `Execute` many times — including on different targets at once — is the intended use.

⚙ `Execute(target)` is the only step with a side effect, and it returns immediately (the pipeline is `async void`). To know a run finished, read the target's real value, or sequence on it with `AwaitThen`.

⚙ **Mutual by default.** `Execute(target)` supersedes the target's running mutual animation. `Execute(target, CanMutualTask: false)` runs alongside instead; concurrent runs are tracked separately and are not cancelled by a later mutual `Execute`.

⚙ **A declaration that reads a local is built per caller.** An endpoint local is passed by value and frozen when the declaration is built, so a cached declaration keeps the first caller's endpoint for every later `Execute`. An index local such as `Items[i]` is captured through the closure instance and read live on every frame, but the closure is shared by the cached declaration, so one later change to `i` drives every execution of it. Both belong in the create-and-discard form.

⚙ `Execute(target, timeline)` anchors several animations to one `ITimeSourceControl` when they must share one transport (pause / rate / seek act on it as a unit). The parameter is positional and named `timeline`. The default implementation is `TimeSourceCore` (`VeloxDev.Timing`), and `TimerCore.CreateTimeSource<ITimeSourceControl>()` resolves it. A tick channel's own source comes from `TickManager.Bus(channel)` (null until that channel has started), which is how an animation shares the frame loop's clock.
