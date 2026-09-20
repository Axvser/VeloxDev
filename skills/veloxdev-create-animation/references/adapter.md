# Building an adapter

A GUI gets support by implementing a handful of small classes against Core's interfaces. Nothing in Core knows about any UI framework — that is the whole reason an unsupported GUI is a contained job rather than a fork.

Put them in a namespace of your choosing and mirror the seven official adapters (`Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`), which are the reference implementations.

An adapter needs three namespaces: `VeloxDev.TransitionSystem` for the types users name, `VeloxDev.TransitionSystem.Abstractions` for the bases it derives from (`InterpolatorCore`, `StateCore`, `TransitionInterpreterCore`, `TransitionHostBase`), and `VeloxDev.Threading` for `NonPriority` and `ThreadRef`.

## 1. `UIThreadInspector` — the only class that needs real platform knowledge

Derive from `TransitionHostBase<TPriorityCore>` and answer **three** members. Everything else — inline-versus-queue, the await-only-when-accepted rule, the blocking read, liveness — lives in the base and in the two subsystems underneath it (`VeloxDev.Threading`, `VeloxDev.Lifetime`), so no adapter can derive it differently from another.

| Member | The question it answers |
|---|---|
| `ThreadRef ThreadFor(object target)` | which thread owns this target, or `ThreadRef.None` |
| `bool IsCurrentThread(ThreadRef thread)` | whether the calling thread is that one |
| `bool PostCore(object target, ThreadRef thread, Action action, TPriorityCore priority)` | the platform's one queue entry point |

**Route by the target, not by a captured global.** If the target is a UI object it already knows its thread, so a background-thread start marshals straight there with no capture and no wrong-thread hop:

```csharp
// WPF — Src/Adapters/VeloxDev.WPF/PlatformAdapters/UIThreadInspector.cs
public override ThreadRef ThreadFor(object target)
{
    try
    {
        var dispatcher = target is DispatcherObject o ? o.Dispatcher
            : System.Windows.Application.Current?.Dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread);
        return ThreadRef.From(dispatcher);
    }
    catch { return ThreadRef.None; }
}
```

WinUI does the same on `DependencyObject.DispatcherQueue`, falling back to a lazily captured global. Jalium chains three levels — the target, then `Application.Current`, then a static main dispatcher — with the reason written down: *"so even POCO targets marshal correctly."* WinForms cannot name a `Control`'s thread, so it overrides `IsCurrent` to ask the Control instead (`!control.InvokeRequired`), which is the same question answered where it is actually known.

**`PostCore` must report acceptance honestly.** It returns `false` when the action could not be queued at all, and that flag is the only thing standing between a dropped frame and a hung pipeline — `PostAsync` only waits on a completion source once the queue reported the work was accepted, so an optimistic `true` for work that was silently dropped hangs the consumer for the rest of the process. Check what your host says when the queue is gone (`HasShutdownStarted`, a refused `TryEnqueue`, a detached handle) and return `false`.

⚙ `IThreadDispatcher.Run<T>` reports the same failure the same way — `default` when the work could not be queued.

**Liveness is a state you have to keep, and it differs per host.** WPF and Avalonia carry none. The others each invent something, and all of them report through `Lifetime` rather than a private flag:

⚙ WinUI reports both directions, so one refused enqueue no longer kills the application for the rest of the process: `Lifetime.SetAlive(queue.TryEnqueue(...))`.

⚙ WinForms subscribes once, on first capture: `Application.ApplicationExit += (_, _) => _isAppAlive = false;`

⚙ MAUI counts windows: `Application.Current?.Windows?.Count > 0`

⚙ Razor has no signal to observe, so it exposes a manual `NotifyShutdown()` for the host to call.

**`IsCurrentThread` is the one predicate the base cannot supply** — it holds an opaque handle and cannot compare it to the calling thread. Default it from the target, which is correct for any GUI: a view must be created on the UI thread, so the thread a target is on and the application's UI thread are the same one. MAUI inverts its own flag (`!dispatcher.IsDispatchRequired`), so an unknown state is treated as *not* the UI thread and the work is dispatched; WinForms and Razor compare the captured `SynchronizationContext` by reference.

**Exception behaviour is uniform, and Core owns it.** A throw from a callback, a sampler or a host ends the run and is reported through the effect's `Error` event; nothing propagates to the animation's caller. Do not add a `catch { }` of your own on the write path — `SamplerSet` already reports and stops the run there, and a second swallow only hides which layer saw the failure.

⚙ **Re-check cancellation after marshaling.** Core does this for you in `SamplerSet`, but if you write your own frame path: the write lands on the UI thread whenever the message is pumped, and the animation may have been cancelled in between. Checking only before queueing lets a stale frame overwrite a reset.

**Which one to copy:** WPF if your host has a dispatcher; WinForms if it does not. Add WinUI's `TaskCompletionSource` read if your host cannot read a property from a background thread directly.

### The frame pacer

`TransitionInterpreterCore.CreateFramePacer` returns a `FramePacerCore` the loop waits through, or null to fall back to the thread-pool timer. Derive the pacer from `affinity.ThreadFor(target)`, never from the platform — a pacer that disagrees with the write path turns every frame into a dispatch, which is the one thing the sampling path avoids.

⚙ **The timer must be repeating.** `Arm` is called once per frame and `Disarm` stops the timer on every tick, so exactly one tick per arm is drawn — but only if the timer re-arms. MAUI's `IDispatcherTimer` with `IsRepeating = false` fires **once and never again**, which silently caps every animation at two frames: values freeze at their start, the closed-form half of the suite still passes, and nothing throws. WinUI's `DispatcherQueueTimer` re-arms correctly with the same flag, so this is a per-host check, not a rule you can copy across.

⚙ **Release the timer in `Dispose`.** The base's `Dispose` only stops the wait and releases the pending continuation; a host timer with its own teardown (WinForms', Avalonia's) is released by an override calling `base.Dispose()` and then detaching its tick and disposing.

## 2. Samplers — where an adapter's real value is

One `ISampler` per platform type, registered as a **stateless singleton**. Three members, and the division of labour between them is the first thing to get right:

⚙ `NormalizeStart` / `NormalizeEnd` run **once per animation**. `InsertFrame` runs **once per frame**. Put anything expensive in the normalizers.

⚙ **Never mutate `start` or `end`** — they are shared with the snapshot. Every scratch in every adapter is a copy of the start, recomputed from the pristine endpoints each frame.

⚙ **`t == 0` and `t == 1` return the caller's own instance, not the scratch.** A nested path such as `((TranslateTransform)x.RenderTransform).X` depends on the runtime type the endpoint was declared with, and the interpolated scratch would replace it. Core guarantees the endpoints are exact, so you only have to hand them back.

⚙ **The scratch (`ref object? working`) is per animation.** Derive its type from the endpoint and re-derive it if the type changes:

```csharp
// Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/TransformSampler.cs
if (working is not Transform wt || wt.GetType() != startT.GetType()) { /* build a fresh scratch */ }
```

#### Colours

⚙ **R/G/B share one bounded progress; alpha is its own range.** Interpolating channels independently lets red saturate while green climbs — which shifts the hue. Use `BoundedProgress(t, 0, 255)` for RGB (`(t, 0, 1)` where channels are floats, as in MAUI) and let alpha ride raw `t`.

⚙ **Saturate, never wrap.** A bare byte cast turns 300 into 44.

⚙ **Overshoot must not be flattened** — that is the whole point of `Back` and `Elastic`.

⚙ WinUI is the only adapter that blends **premultiplied** (un-premultiply by the interpolated alpha at the end), and it deliberately takes the progress from the *visible* colours rather than from the premultiplied channels, because each premultiplied channel carries alpha inside it and a hue cannot be bounded there.

#### Sizes and lengths

⚙ **Width and height share one zero-floored progress**, so an overshoot cannot skew the shape — a negative size is not representable. Position stays unbounded.

⚙ Integer and pixel types floor **after** rounding (`Math.Max(0, …)`).

⚙ Some framework types validate in their constructor and *throw* rather than truncate — `GridLength` is one — so clamp before you build the value.

⚙ **Corner radii are the exception: clamp each corner independently.** They are not a proportion — one corner reaching zero has nothing to do with the others, and forcing them to move together needlessly freezes the ones that could still shrink. WinUI does this and says so; the other four adapters do not, and WinUI is the one to copy.

#### Values that are discrete

Some values have no meaningful midpoint — a unit kind, an inset flag, a shadow's brush. Switch at a threshold instead of interpolating (`t >= 0.5 ? end : start`, or `t >= 1` when the value is only valid at an endpoint). When two endpoints are not comparable at all, prefer the branch that still reaches the target over the one that holds the start — WinUI's `GridLengthSampler` switches to the end value, Avalonia's holds the start value; only the former ever arrives.

#### Reference-type products (brushes, transforms, effects)

The endpoints are factories, and what you do in between depends on whether the two are comparable:

⚙ **Same concrete type and same shape** (equal gradient stop counts, for instance) → interpolate field by field into the scratch. Gradient stop **offsets share one bounded progress** so the stops keep their spacing instead of crossing — crossing inverts the gradient.

⚙ **Different concrete types** → you must decide, and the answers differ. The clean one is a real cross-fade: WPF renders each endpoint to a bitmap and blends by opacity — the blend factor is *clamped*, because a cross-fade cannot express an overshoot; it saturates at either end. The cheap one is to pick one representative colour and put it in a scratch solid, which every other adapter does.

⚙ **Pick the representative stop deliberately.** WinUI and Jalium take the **last** stop; Avalonia and MAUI take the **first**.

⚙ **Know your host's dead ends before you choose.** Jalium has no compositing brush, and its source documents the two routes that do not work: `RenderTargetBitmap`'s drawing context accepts only solid colours (gradients draw nothing, and `PushOpacity`/`DrawImage` are stubs, leaving the bitmap empty), and `DrawingBrush` renders nothing at all — measured at 0 of 9800 pixels. Either route makes overshoot frames *absent*, and an invisible frame cannot be told apart from an animation that never ran.

⚙ **Transforms**: keep the last transform of each type inside a group, take the union of types across both endpoints, pair by type, interpolate, recombine; on a type mismatch fall back to element-wise matrix lerp. Directional angles need their own helper (wrap the delta into ±360). **Custom transform subclasses must be excluded from the clone fast path** — they are the one case where cloning throws, so gate the fast path on the exact known types. And **never wrap a single transform in a `TransformGroup`**: it changes the runtime type and breaks nested paths; wrap only when there really are several.

## 3. Registration

Core pre-registers everything framework-free (the primitives, `System.Drawing` structs, `System.Numerics` types), so register only what your framework owns:

```csharp
public class Interpolator : InterpolatorCore
{
    static Interpolator()
    {
        RegisterInterpolator(typeof(Brush), new BrushSampler());
        // …whatever this framework owns
    }

    // The platform's priority type — see [Priority](#4-priority).
    public override TransitionSchedulerCore? CreateScheduler(object target, ITransitionEffectCore effect)
        => effect is ITransitionEffect<MyPriority>
            ? (TransitionSchedulerCore)TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, MyPriority>.FindOrCreate(target)
            : null;
}
```

⚙ **`CreateScheduler` is what lets a theme switch animate on your platform.** A theme switch runs on the transition system rather than on a timer of its own, and this is the one thing Core cannot answer for it: which inspector, interpreter and priority make up a scheduler, for a target Core only knows as an `object`. Leave it out and the switch still happens — **instantly**, with nothing logged. The same silent degradation as a missing `SetPlatformInterpolator`.

⚙ **Go through `FindOrCreate`, never a `new` scheduler.** Only `FindOrCreate` files it under the target, and that registration is what a later `Transition.Pause` / `Seek` / `Exit(target)` reads. A scheduler constructed directly animates correctly and can never be controlled.

⚙ Returning null is a legitimate answer — it is how a platform says "this effect is not mine". The effect test mirrors the cast the scheduler itself performs before running, so leaving it out gets you a scheduler that silently draws nothing.

⚙ **Register the type you actually mean; the lookup walks up for you.** A property declared as `LinearGradientBrush` finds the sampler registered for `Brush`, and one declared as `SolidColorBrush` finds the one registered for `IBrush`. The walk is: exact type, then base classes nearest-first, then interfaces (ordered by name when several match).

⚙ That makes a registration for a concrete type *redundant* when its base or interface is already registered — Jalium registers both `Brush` and `SolidColorBrush`, which still works but no longer carries anything the base registration does not. Register the general type unless a concrete one genuinely needs its own sampler.

⚙ **The walk only goes up, so a concrete registration leaves base-declared properties unserved.** A property declared as `Effect` never reaches a sampler registered for `DropShadowEffect` — not a near miss, no sampler at all: `Prepare` reports the path `Unsampled` and drops it, with nothing thrown and no fallback. Register the type your users will *declare*; if that is the abstract base, the sampler has to handle the whole family (the next bullet) rather than the one subclass it was written for. WPF is the worked example of both halves: its `UIElement.Effect` DP is declared `Effect`, so it registers `typeof(Effect)` and its `DropShadowEffectSampler` serves the family — pairing two `DropShadowEffect`s field by field, and switching at `t >= 0.5` for a pair it cannot compare (a `BlurEffect` reaches it too, and must be handed back rather than replaced by a stand-in of the sampler's making).

⚙ **The key's type is the type your sampler gets handed — check that line against the cast in the sampler's body.** Two types sharing a *simple name* in different assemblies are two different keys and coexist happily; what breaks is a sampler that unboxes the *other* assembly's type, because the first frame throws `InvalidCastException` and the run is cancelled rather than degraded. Nothing catches this at compile time, and the coverage check — written against sampler *type sets* — cannot see a `(key, sampler)` mismatch; the acceptance suite's key check can, because it resolves each entry's declared type against the live registry. A registration whose key is `typeof(RectF)` next to a body of `(RectangleF)(…)` is the shape to watch for — spell the sampler's own using list out if the framework also ships same-named geometry.

⚙ **A more general registration must handle the whole family.** Registering `Brush` means your sampler will be handed gradients, not only solids; `Size` means `SizeF` is not covered (it has its own registration), but a custom subclass of a registered type will reach you. Handle the subclasses or the paths that use them will misbehave rather than fail loudly. For a family member you genuinely cannot interpolate, hand back one of the endpoints you were given — building a stand-in of the type your sampler knows best turns a visible no-op into a wrong animation that nothing reports.

## 4. Priority

Use the framework's dispatcher-priority type when it has one, and `NonPriority` when it does not.

| Adapter | `TPriorityCore` |
|---|---|
| WPF, Avalonia, Jalium | `DispatcherPriority` |
| WinUI | `DispatcherQueuePriority` |
| MAUI, WinForms, Blazor | `NonPriority` |

⚙ The priority type is used in **two** places, and the table above covers both: the seventh type argument of `Transition<T>`, and the type `CreateScheduler` tests the effect against. Get the second one wrong and the effect never matches — a theme switch degrades to instant switching instead of failing.

⚙ `NonPriority` costs nothing per frame: the sampling loop stays on the priority-free path. Carrying the priority as a type parameter rather than an `object?` is itself the point — it stopped a `DispatcherPriority` being boxed on every frame of every animation.

⚙ Pick a sensible default for `TransitionEffect.Priority`. The adapters that have one default to `DispatcherPriority.Render`; WinUI explains its choice — queue animation frames at high priority so they are processed before rendering.

## 5. Traps

⚙ **Constructor argument order is not universal.** MAUI's `CornerRadius` takes `(topLeft, topRight, bottomLeft, bottomRight)`, matching its property order; WinUI's takes `(topLeft, topRight, bottomRight, bottomLeft)`. Copy one into the other and the bottom two corners are swapped — wrong from the very first frame.

⚙ **A shared scratch silently pins the start's properties.** Where a value carries an axis or a mode that the scratch keeps from the start, two different endpoints settle on *neither* — the start's axis at the end's angle. The axis has to be part of the guard, not only the angle.

## Verifying an adapter

⚙ Copy the shape of `Examples/Transition/<GUI>/Demo`: a window listing one case per sampler, a case that starts a real transition on a real control, and a payload the automation can read.

⚙ If the project also runs the acceptance suite, an adapter is registered in two places — `Drivers/DemoCatalog.cs` (one line) and `Suites/PlatformSuites.cs` (one class that opens and closes the demo). `Examples/Transition/AUTO TEST/AGENTS.md` is the operating guide.

⚙ The acceptance suite verifies **every published sampler** (`Examples/Transition/AUTO TEST/Samplers/`): 69 of the 75 against a closed form, and the remaining six — WinUI's and MAUI's framework-object samplers, which cannot be constructed in a process with no XAML/MAUI runtime — in a real running app by the AT suite. A coverage test fails if a published sampler has no entry in either list. Adding a sampler means adding its row there — which is how these rules were checked in the first place.

⚙ Each row also carries the **declared type of the property it animates**, and that type is the registration key. The suite resolves it against the live registry (it runs your adapter's `Interpolator` static constructor to get one) and fails when the key lands on a different sampler or on none at all. So a row whose declared type is not the one you registered goes red rather than quietly animating nothing — and the registry key is the one thing the closed-form half, which picks its own endpoints, cannot check.
