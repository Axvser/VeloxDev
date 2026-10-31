# Building an adapter

A GUI gets support by implementing a handful of small classes against Core's interfaces. Nothing in Core knows about any UI framework — that is why an unsupported GUI is a contained job rather than a fork.

Put them in a namespace of your choosing and mirror the seven official adapters (`Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`), which are the reference implementations.

An adapter needs three namespaces: `VeloxDev.TransitionSystem` for the types users name, `VeloxDev.TransitionSystem.Abstractions` for the bases it derives from (`InterpolatorCore`, `StateCore`, `TransitionInterpreterCore`, `TransitionHostBase`), and `VeloxDev.Threading` for `NonPriority` and `ThreadRef`.

## 1. `UIThreadInspector` — the only class that needs real platform knowledge

Derive from `TransitionHostBase<TPriorityCore>` and answer **three** members. Everything else — inline-versus-queue, the await-only-when-accepted rule and the blocking read — lives in the base and in `VeloxDev.Threading` / `VeloxDev.Lifetime`.

| Member | The question it answers |
|---|---|
| `public override ThreadRef ThreadFor(object target)` | which thread owns this target, or `ThreadRef.None` |
| `protected override bool IsCurrentThread(ThreadRef thread)` | whether the calling thread is that one |
| `protected override bool PostCore(object target, ThreadRef thread, Action action, TPriorityCore priority)` | the platform's one queue entry point |

`IsCurrentFor(object target, ThreadRef thread)` is a `protected virtual` fourth member, not abstract: override it when the target can answer more precisely than the opaque handle — WinForms does (`!control.InvokeRequired`), and the same answer is then handed to `PostCore`.

⚙ **Override `InternalPriority` when your priority type is not `NonPriority`.** It is the priority the blocking `Run<T>` read posts at, and `default(DispatcherPriority)` is `Inactive` — the read would park behind every normal message. WPF, Avalonia and Jalium return `Send`; WinUI returns `Normal`; MAUI, WinForms and Razor leave the default.

**Route by the target, not by a captured global.** If the target is a UI object it already knows its thread, so a background-thread start marshals straight there with no capture and no wrong-thread hop:

```csharp
// WPF — Src/Adapters/VeloxDev.WPF/PlatformAdapters/UIThreadInspector.cs
public override ThreadRef ThreadFor(object target)
{
    try
    {
        var dispatcher = target is DispatcherObject dispatcherObject
            ? dispatcherObject.Dispatcher
            : System.Windows.Application.Current?.Dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread);
        return ThreadRef.From(dispatcher);
    }
    catch (Exception) { return ThreadRef.None; }
}
```

WinUI does the same on `DependencyObject.DispatcherQueue`, falling back to a lazily captured global. Jalium chains four levels — the target, `Application.Current`, the calling thread, then `Dispatcher.MainDispatcher` — so even a plain object falls back to the app's main dispatcher. WinForms cannot name a `Control`'s thread, so it overrides `IsCurrentFor` to ask the Control instead (`!control.InvokeRequired`).

**`PostCore` reports acceptance honestly.** It returns `false` when the action could not be queued at all, and that flag is the only thing standing between a dropped frame and a hung pipeline — `PostAsync` only waits on a completion source once the queue reported the work was accepted. Check what your host says when the queue is gone (`HasShutdownStarted`, a refused `TryEnqueue`, a detached handle) and return `false`.

**Razor is the documented exception.** `SynchronizationContext.Post` reports nothing — it returns `void` and neither throws nor signals when the circuit's queue is gone — so Razor's `PostCore` returns `true` unconditionally once it has a context. The one half it can report is a `ThreadRef` with no `SynchronizationContext`, which returns `false`. This is a platform limitation. Its mitigations are the stale-frame guards in `SamplerSet.Apply`, which drop a late frame, and the run's cancellation token / the host's `Dispose` as the only reliable "still running" signal.

⚙ `IThreadDispatcher.Run<T>` reports the same failure the same way — `default` when the work could not be queued.

**Liveness is a state the host keeps, and it differs per host.** WPF and Avalonia carry none (the base's `Lifetime` stays alive). The others each invent something: WinUI reports into `Lifetime` on every enqueue, while MAUI, WinForms and Razor override `IsAlive` from their own signal.

⚙ WinUI reports both directions: `Lifetime.SetAlive(queue.TryEnqueue(...))`.

⚙ WinForms subscribes once, on first capture: `Application.ApplicationExit += (_, _) => _isAppAlive = false;`.

⚙ MAUI counts windows: `Application.Current?.Windows?.Count > 0`.

⚙ Razor has no signal to observe, so it exposes a manual `NotifyShutdown()` for the host to call.

⚙ **Explicit capture is the fallback.** WinUI, WinForms and Razor expose `UIThreadInspector.CaptureUIThread()` for the first start from a background thread of a target that cannot name its own thread. WPF and MAUI resolve from the target or `Application.Current`; Avalonia answers `Dispatcher.UIThread` unconditionally, which is the same thread for a single-dispatcher application.

**`IsCurrentThread` is the one predicate the base cannot supply** — it holds an opaque handle and cannot compare it to the calling thread. Default it from the target, which is correct for any GUI: a view is created on the UI thread, so the thread a target is on and the application's UI thread are the same one. MAUI inverts its own flag (`!dispatcher.IsDispatchRequired`), so an unknown state is treated as *not* the UI thread and the work is dispatched; WinForms and Razor compare the captured `SynchronizationContext` by reference.

**Exception behaviour is uniform, and Core owns it.** A throw from a callback, a sampler or a host ends the run and is reported through the effect's `Error` event; nothing propagates to the animation's caller. `SamplerSet` reports and stops the run on the write path; a second `catch` there hides which layer saw the failure.

⚙ **Re-check cancellation after marshaling.** Core does this in `SamplerSet`; a custom frame path does the same. The write lands on the UI thread whenever the message is pumped, and the animation may have been cancelled in between; checking only before queueing lets a stale frame overwrite a reset.

**Which one to copy:** WPF if your host has a dispatcher; WinForms if it does not. The blocking read is not yours to write — `ThreadDispatcherBase.Run<T>` posts and waits on a `TaskCompletionSource` for every host, so overriding `ThreadFor` and (where needed) `IsCurrentFor` is enough.

### The frame pacer

`TransitionInterpreterCore.CreateFramePacer` returns a `FramePacerCore` the loop waits through, or null to fall back to the thread-pool timer. The invariant is that the pacer runs on **the same thread the write path resolves**: derive it from `affinity.ThreadFor(target)`. A pacer that disagrees with the write path turns every frame into a dispatch, which is the one thing the sampling path avoids. WinForms shows both halves: `CreateFramePacer` returns its pacer only when `affinity.IsCurrent(target)` and null otherwise; the pacer itself is a thread-pool timer whose tick posts the frame with `Control.BeginInvoke`, so the writes stay direct and on the control's thread. The null path is the documented fallback, where each write marshals itself.

⚙ **The timer re-arms on every `Arm`.** `Arm` is called once per frame and `Disarm` stops the timer on every tick, so exactly one tick per arm is drawn — provided a later `Start()` fires again. MAUI's `IDispatcherTimer` fires **once** when non-repeating, which silently caps every animation at two frames; MAUI creates it with `IsRepeating = true`. WinUI's `DispatcherQueueTimer` re-arms correctly with `IsRepeating = false`. Which value is right is a per-host check, not a rule you can copy across.

⚙ **Release the timer in `Dispose`.** `FramePacerCore.Dispose` stops the wait and releases the pending continuation; a host timer with its own teardown (WPF's, Avalonia's, Jalium's, WinUI's, MAUI's, WinForms') is released by an override calling `base.Dispose()` and then detaching its tick and disposing.

## 2. Samplers — where an adapter's real value is

One `ISampler` per platform type, registered as a **stateless singleton**. Three members, and the division of labour between them is the first thing to get right:

⚙ `InterpolatorCore.Prepare<TPriorityCore>(object target, IFrameState state, ITransitionEffectCore effect, ITransitionHost<TPriorityCore> host)` is the settlement point: it reads each declared path on the owning thread, binds `PathIndex.Frozen` arguments once, resolves the sampler (custom → registry → `ISampleable`) and stores both normalized endpoints. Override it only to change that chain — and call the base if you do.

⚙ `NormalizeStart` / `NormalizeEnd` run **once per segment**; `InsertFrame` runs **once per frame**. Put anything expensive in the normalizers.

⚙ **`start` and `end` are shared with the snapshot.** Every scratch in every adapter is a copy of the start, recomputed from the pristine endpoints each frame.

⚙ **When the product feeds a nested path, `t == 0` and `t == 1` hand back the caller's own instance, not the scratch.** A path such as `((TranslateTransform)x.RenderTransform).X` depends on the runtime type the endpoint was declared with; the interpolated scratch would replace it. The transform samplers early-return `start` / `end` at exactly those times.

⚙ **The scratch (`ref object? working`) is per segment**, like the endpoints it was built from. Derive its type from the endpoint and re-derive it if the type changes:

```csharp
// Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/TransformSampler.cs
if (working is not Transform wt || wt.GetType() != startT.GetType()) { /* build a fresh scratch */ }
```

### Colours

⚙ **R/G/B share one bounded progress; alpha is its own range.** Interpolating channels independently lets red saturate while green climbs — which shifts the hue. Use `BoundedProgress(t, 0, 255)` for RGB (`(t, 0, 1)` where channels are floats, as in MAUI) and let alpha ride raw `t`.

⚙ **Saturate.** A bare byte cast turns 300 into 44.

⚙ **Preserve overshoot.** `Back` and `Elastic` are meant to leave `[0,1]`.

⚙ WinUI is the only adapter that blends **premultiplied** (un-premultiply by the interpolated alpha at the end), and it takes the progress from the *visible* colours rather than from the premultiplied channels, because each premultiplied channel carries alpha inside it.

### Sizes and lengths

⚙ **Width and height share one zero-floored progress**, so an overshoot cannot skew the shape. Position stays unbounded.

⚙ Core's `IntSampler` rounds to the nearest integer; `LongSampler` truncates (a `decimal` intermediate cast to `long`). Pixel types truncate as well (`(int)(delta * progress)`) and clamp sizes at zero (`Math.Max(0, …)`); pixel positions stay unbounded.

⚙ Some framework types validate in their constructor and *throw* rather than truncate — `GridLength` is one — so clamp before you build the value.

⚙ **Corner radii clamp each corner independently.** They are not a proportion — one corner reaching zero has nothing to do with the others. WinUI does this; it is the one to copy.

### Values that are discrete

Some values have no meaningful midpoint — a unit kind, an inset flag, a shadow's brush. Switch at a threshold instead of interpolating (`t >= 0.5 ? end : start`, or `t >= 1` when the value is only valid at an endpoint). When two endpoints are not comparable at all, choose the branch that reaches the target: WinUI's `GridLengthSampler` switches to the end value; Avalonia's holds the start value.

### Reference-type products (brushes, transforms, effects)

The endpoints are factories, and what you do in between depends on whether the two are comparable:

⚙ **Same concrete type and same shape** (equal gradient stop counts, for instance) → interpolate field by field into the scratch. Gradient stop **offsets share one bounded progress** so the stops keep their spacing instead of crossing.

⚙ **Different concrete types** → choose deliberately. A real cross-fade renders each endpoint to a bitmap and blends by opacity; the blend factor is clamped, because a cross-fade cannot express an overshoot. The cheap path picks one representative colour and puts it in a scratch solid.

⚙ **Pick the representative stop deliberately.** WinUI and Jalium take the **last** stop; Avalonia and MAUI take the **first**.

⚙ **Know your host's dead ends before you choose.** Jalium has no compositing brush, and its source documents the two routes that do not work: `RenderTargetBitmap`'s drawing context accepts only solid colours (gradients draw nothing, and `PushOpacity`/`DrawImage` are stubs, leaving the bitmap empty), and `DrawingBrush` renders nothing at all — measured at 0 of 9800 pixels. Either route makes overshoot frames *absent*, and an invisible frame cannot be told apart from an animation that never ran.

⚙ **Transforms**: keep the last transform of each type inside a group, take the union of types across both endpoints, pair by type, interpolate, recombine; on a type mismatch fall back to element-wise matrix lerp. Directional angles need their own helper (wrap the delta into ±360). **Gate the clone fast path on the exact known types** — Avalonia, Jalium and WinUI do (`IsKnownTransform`), because their `CloneTransform` has no case for a custom subclass. WPF instead falls back to `Freezable.Clone()`, and its `MutateInPlace` has no case for the subclass, so the scratch is written back unmutated; exclude unknown subclasses in your own adapter. **Wrap a single transform in a `TransformGroup` only when there really are several** — a group changes the runtime type and breaks nested paths.

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

⚙ **`CreateScheduler` is what lets a theme switch animate on your platform.** A theme switch runs on the transition system rather than on a timer of its own, and this is the one thing Core cannot answer for it: which inspector, interpreter and priority make up a scheduler, for a target Core only knows as an `object`. Leave it out and the switch still happens — **instantly**, with nothing logged. The host must also call `ThemeManager.SetPlatformInterpolator(new Interpolator())` once at startup (the WPF demo does it in `Examples/Theme/WPF/Demo/App.xaml.cs`). Without that call Core never reaches `CreateScheduler`.

⚙ **Go through `FindOrCreate`.** Only that call files the scheduler under the target, and that registration is what a later `Transition.Pause` / `Seek` / `Exit(target)` reads. A scheduler constructed directly animates correctly and can never be controlled.

⚙ Returning null is a legitimate answer — it is how a platform says "this effect is not mine". The effect test mirrors the cast the scheduler itself performs before running, so leaving it out leaves a scheduler that silently draws nothing.

⚙ **Register the type you actually mean; the lookup walks up for you.** A property declared as `LinearGradientBrush` finds the sampler registered for `Brush`, and one declared as `SolidColorBrush` finds the one registered for `IBrush`. The walk is: exact type, then base classes nearest-first, then interfaces (ordered by name when several match).

⚙ A registration for a concrete type is redundant when its base or interface is already registered — it still works but carries nothing extra. Register the general type unless a concrete one genuinely needs its own sampler.

⚙ **The walk only goes up, so a concrete registration leaves base-declared properties unserved.** A property declared as `Effect` never reaches a sampler registered for `DropShadowEffect`; `Prepare` reports the path `Unsampled` and drops it, with nothing thrown and no fallback. Register the type your users will *declare*; if that is the abstract base, the sampler handles the whole family. WPF is the worked example: its `UIElement.Effect` DP is declared `Effect`, so it registers `typeof(Effect)` and its `DropShadowEffectSampler` serves the family — pairing two `DropShadowEffect`s field by field, and switching at `t >= 0.5` for a pair it cannot compare.

⚙ **The key's type is the type your sampler gets handed — check that line against the cast in the sampler's body.** Two types sharing a *simple name* in different assemblies are two different keys and coexist happily; what breaks is a sampler that unboxes the *other* assembly's type, because the first frame throws `InvalidCastException` and the run is cancelled rather than degraded. The acceptance suite's key check resolves each entry's declared type against the live registry.

⚙ **A more general registration must handle the whole family.** Registering `Brush` means your sampler will be handed gradients, not only solids; `Size` does not cover `SizeF` (it has its own registration), but a custom subclass of a registered type will reach you. Handle the subclasses. For a family member you genuinely cannot interpolate, hand back one of the endpoints you were given.

## 4. Priority

Use the framework's dispatcher-priority type when it has one, and `NonPriority` when it does not.

| Adapter | `TPriorityCore` |
|---|---|
| WPF, Avalonia, Jalium | `DispatcherPriority` |
| WinUI | `DispatcherQueuePriority` |
| MAUI, WinForms, Blazor | `NonPriority` |

⚙ The priority type is used in **two** places, and the table above covers both: the `TPriorityCore` argument of the core base `TransitionCore<T, TStateCore, TEffectCore, TInterpolatorCore, THost, TTransitionInterpreterCore, TPriorityCore>` (the seventh in that base's list, which each adapter closes over in `Transition<T>`), and the type `CreateScheduler` tests the effect against.

⚙ `NonPriority` costs nothing per frame: the sampling loop stays on the priority-free path. Carrying the priority as a type parameter rather than an `object?` is itself the point — it stops a `DispatcherPriority` being boxed on every frame of every animation.

⚙ Pick a sensible default for `TransitionEffect.Priority`. The adapters that have one default to `DispatcherPriority.Render`; WinUI queues animation frames at high priority so they are processed before rendering.

## 5. Traps

⚙ **Constructor argument order is not universal.** MAUI's `CornerRadius` takes `(topLeft, topRight, bottomLeft, bottomRight)`, matching its property order; WinUI's takes `(topLeft, topRight, bottomRight, bottomLeft)`. Copy one into the other and the bottom two corners are swapped — wrong from the very first frame.

⚙ **A shared scratch pins the start's axis.** Where a value carries an axis or a mode that the scratch keeps from the start, two different endpoints settle on *neither* — the start's axis at the end's angle. The axis belongs in the guard, not only the angle.

## Verifying an adapter

⚙ Copy the shape of `Examples/Transition/<GUI>/Demo`: a window listing one case per sampler, a case that starts a real transition on a real control, and a payload the automation can read.

⚙ If the project also runs the acceptance suite, an adapter is registered in two places — `Drivers/DemoCatalog.cs` (one line) and `Suites/PlatformSuites.cs` (one class that opens and closes the demo). `Examples/Transition/AUTO TEST/AGENTS.md` is the operating guide.

⚙ The acceptance suite verifies **every published sampler** (`Examples/Transition/AUTO TEST/Samplers/`): most against a closed form, and the framework-object samplers that cannot be constructed in a process with no XAML/MAUI runtime in a real running app by the AT suite. A coverage test fails if a published sampler has no entry in either list. Adding a sampler means adding its row there.

⚙ Each row also carries the **declared type of the property it animates**, and that type is the registration key. The suite resolves it against the live registry (it runs your adapter's `Interpolator` static constructor to get one) and fails when the key lands on a different sampler or on none at all.

When an adapter drops frames, hangs, or animates at two frames, read [references/pitfalls/adapter.md](../pitfalls/adapter.md).
