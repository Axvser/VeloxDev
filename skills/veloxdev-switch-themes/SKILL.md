---
name: veloxdev-switch-themes
description: Give a VeloxDev app runtime themes that can animate when they change — declare a theme class, mark properties with [ThemeConfig], call InitializeTheme(), and switch with SetCurrent, Jump or Transition
---

## Responsibility

Let a property's value depend on which theme is current, and switch themes at runtime — instantly or animated — without writing per-theme branches anywhere in the view.

A themeable property is declared by naming it in an attribute **on the class**, together with the value it takes in each theme. The generator wires the rest: registration, applying the current theme at start-up, and the machinery a switch drives.

## Which packages

Two, and both are needed:

| | provides |
|---|---|
| `VeloxDev.Core` | `ITheme`, the theme classes, `[ThemeConfig]`, `ThemeManager`, and the generator |
| one GUI adapter (`VeloxDev.WPF`, `VeloxDev.Avalonia`, `VeloxDev.WinUI`, `VeloxDev.MAUI`, `VeloxDev.WinForms`, `VeloxDev.Razor`) | the **converters** that turn a theme's parameters into that framework's value type — `BrushConverter` and friends |

Core cannot do it alone: a theme's parameters are plain values (a hex string), and only the adapter knows how to make a `System.Windows.Media.Brush` or an `Avalonia.Media.IBrush` out of one. It is also what supplies the `Interpolator` you register for animated switching — and, through it, the scheduler a switch runs on.

⚙ **Jalium is the exception.** It ships the transition `Interpolator` but no `ThemeValueConverters.cs`, so `[ThemeConfig]` has no shipped converter type to name there. A project on Jalium can still write its own `IThemeValueConverter`.

## A theme is an empty marker

```csharp
public class Light : ITheme { }
```

That is the whole class — `Dark` and `Light` ship with Core, and a custom theme is written the same way. **The values do not live on the theme**; they live in the attributes that name it.

## Declaring a themeable property

The attribute goes on the **class** — once per property — and names the property as a string:

```csharp
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
public partial class MainWindow { … }
```

⚙ The type parameters are **one converter followed by the themes**; the constructor arguments are the property name followed by **one argument array per theme, in the same order**. Position is the pairing — a theme listed second takes the second array, so swapping two theme type arguments silently swaps their values.

⚙ **At least two themes, and at most six.** The attribute family declares arities up to seven themes, but the generator only *discovers* classes that carry a two-to-six-theme row. A seven-theme row is emitted only when the same class also has a row in that range; a class whose only rows are seven-theme rows compiles and then **generates nothing at all**, silently, exactly like a missing `partial`. Stay at six or fewer.

⚙ The converter comes from the adapter package and is instantiated by the generator, so it needs a **public parameterless constructor**.

⚙ The property is matched **by name at build time**. A name that does not resolve is dropped without a diagnostic — if a property never changes with the theme, check the spelling before anything else.

⚙ Put the theme declarations in their **own `partial` class block**. They are configuration, not interaction logic, and keeping them apart is what the shipped demo does:

```csharp
public partial class MainWindow : Window   // interaction
{
    public MainWindow() { InitializeComponent(); LoadTheme(); }
}

[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
public partial class MainWindow            // theme
{
    private void LoadTheme() { … }
}
```

## Turning it on

```csharp
private void LoadTheme()
{
    InitializeTheme();   // required, and must come after InitializeComponent()

    // Required for an animated switch; without it a switch still happens, immediately.
    ThemeManager.SetPlatformInterpolator(new Interpolator());

    // Where an animated switch takes its starting value from.
    ThemeManager.StartModel = StartModel.Cache;
}
```

⚙ **`InitializeTheme()` is generated and must be called.** It registers the object, applies the current theme's values to its properties, and registers the declared values for its type. You do not call `Register` yourself; `Unregister` is the explicit way to stop tracking an instance before it is collected.

⚙ **It must come after `InitializeComponent()`.** Converters look up application resources, and those are not populated until the view is built.

⚙ **The class must be `partial`.** A non-partial class gets no generated code at all — and no error, so the theme simply never works.

⚙ `SetPlatformInterpolator` is **mandatory for animated switching**. It is what lets the switch run on the adapter's `TransitionSchedulerCore` — the same one a `Transition<T>` run uses. Without it the switch is not animated **at all**: every value is written at once, the same as `Jump`. Nothing warns, so a switch that looks like "the theme has no animation" is usually this line.

⚙ A property whose type has **no sampler** is a separate, narrower case: that one property keeps its old value for the whole switch and jumps at the end, while its neighbours animate normally.

⚙ `StartModel` picks where an animated switch starts from: `Cache` (the cached value for the current theme — the instance override first, then the declared value) or `Reflect` (read the live property). Both settings are global.

⚙ `StartModel.Reflect | StartModel.Cache` is a legal `[Flags]` value but **not a usable mode**: `PrepareSamplers` switches on the exact values, matches neither arm, and skips every property. Use one value at a time.

## Switching

```csharp
ThemeManager.SetCurrent<Light>();                        // set Current only: no values, no hooks
ThemeManager.Jump<Light>();                              // switch now, synchronously, without animating
ThemeManager.Transition<Light>(TransitionEffects.Theme);  // start an animated switch
// Transitions and jumps to the theme already current are ignored, with a Debug.WriteLine.

var current = ThemeManager.Current;                      // a Type; defaults to typeof(Dark)
```

⚙ `Current` is a `Type`, not an instance — compare with `ThemeManager.Current == typeof(Dark)`. `SetCurrent<T>` changes only that property; it is for seeding the starting theme before elements register, not for a visible switch.

⚙ `Transition<T>` is `async void`: it returns after preparing the switch, and `Current` plus `OnThemeChanged` move only when the animation actually lands. There is nothing to await. An exception inside the run is logged rather than thrown to the caller, but one from your `OnThemeChanging` / `OnThemeChanged` hook is not caught.

⚙ `TransitionEffects.Theme` is the adapter's ready-made effect for this; reach for it rather than building one, and swap in your own `TransitionEffect` only when you want a different duration or curve. The scheduler accepts any `ITransitionEffect<TPriority>` for its framework — `DispatcherPriority` for WPF, Avalonia and Jalium, `DispatcherQueuePriority` for WinUI, `NonPriority` for MAUI, WinForms and Razor. A foreign effect makes `CreateScheduler` return null and the whole switch fall back to an instant apply.

⚙ **The effect's other settings are honoured**, not just `Duration` and `Ease`: `FPS` caps the sample rate, `IsAutoReverse` adds a reverse pass, and `LoopTime` adds that many extra iterations of the run (0 is one iteration). A switch with `LoopTime = 1` and `IsAutoReverse = true` therefore settles back on the **start** value — that is the flags working, not a bug.

⚙ **The effect is read by every target, every frame, for the whole switch.** Pass a `TransitionEffects` instance or a fresh `TransitionEffect`, and do not mutate or subscribe to one while a switch that uses it is in flight — a handler on `TransitionEffects.Theme` fires once per target per frame.

## Controlling a switch in flight

One switch drives every registered target through a **single shared timeline**. So the timeline control the transition system already exposes reaches it unchanged — call it on any one of the targets and the whole switch moves, because there is only one transport to move:

```csharp
Transition.Pause(mainWindow);                       // freeze the whole switch
Transition.Resume(mainWindow);
Transition.SetRate(mainWindow, 0.5);                // half speed; 0 freezes the clock, IsPaused stays false
Transition.Seek(mainWindow, TimeSpan.FromSeconds(2)); // jump to 2s in, keeping the rate
Transition.IsPaused(mainWindow);                    // true only when every run on the target is paused
Transition.Rate(mainWindow);                        // the configured rate
Transition.Cycle(mainWindow);                       // the pass index
Transition.Position(mainWindow);                    // how far into the current pass
Transition.Exit(mainWindow);                        // stop it outright, leaving values where they are
```

⚙ These statics are declared on `TransitionCore` (`VeloxDev.TransitionSystem.Abstractions`); the adapter's non-generic `Transition` inherits them, so there is nothing theme-specific to call.

⚙ `Exit` leaves the switch cancelled, so `Current` stays on the old theme and `OnThemeChanged` does not fire. The next animated switch writes its configured start values back onto the targets before animating.

⚙ A pause costs **no timer wake-ups** — the sampling loop parks on a signal — and the paused interval is excluded from the animation rather than merely skipped, so a pause of any length leaves the remaining duration unchanged.

⚙ Seeking past the end of a pass **finishes** it, landing exactly on the endpoint, rather than being clamped.

## Reacting to a change

⚙ **There is no event to subscribe to.** The generator declares two partial methods on your class; implement them and they are called:

```csharp
partial void OnThemeChanged(Type? oldValue, Type? newValue) { … }
partial void OnThemeChanging(Type? oldValue, Type? newValue) { … }
```

⚙ `OnThemeChanging` runs before any value moves. `OnThemeChanged` runs only for a switch that lands — a cancelled, superseded or `Exit`-ed switch announces nothing.

## Editing a theme's values at runtime

The generator also emits a set of accessors on the class:

```csharp
SetThemeValue<Light>(nameof(Background), new SolidColorBrush(Colors.White)); // store a value for Light
RestoreThemeValue<Light>(nameof(Foreground));                                // drop the override

var declared = GetStaticThemeCache();   // what the attributes declared
var overrides = GetActiveThemeCache();  // what has been changed at runtime
```

⚙ **The value is stored verbatim — no converter runs.** `SetThemeValue<T>` is generic only to name the theme; its `newValue` is `object?`. Pass the type the property itself expects (for a WPF `Background`, a `Brush`). A raw `[ThemeConfig]` context array such as `new object?[] { "#fff4d6" }` is stored as data, and the immediate re-apply write is skipped because the array is not assignable to the property. The shipped demo does pass an array, which is why that edit does not change the brush.

⚙ `RestoreThemeValue<T>` removes the property's whole override entry — every theme, not only `T` — then re-applies the value for the current theme.

⚙ **An override wins over the declared value**, and only the properties you changed appear in the active cache. An override stored for a non-current theme takes effect on the next switch to that theme.

⚙ Both caches are keyed `property name → property → theme → value`, so they can be walked for anything the built-in accessors do not cover.

## Pitfalls

⚙ **Switch on the UI thread.** Frames are marshalled through the adapter's `UIThreadInspector` — WPF's `ThreadFor` resolves a `DispatcherObject` target to that object's own `Dispatcher`, and `PostCore` posts each write through `Dispatcher.InvokeAsync`. The sampling loop itself is **not** pinned to a `SynchronizationContext`; it can resume on a pool thread, and the effect's `Update` / `LateUpdate` callbacks run on whatever thread it resumed on. What is not marshalled is the synchronous start of the switch: `OnThemeChanging` and the write of each property's **starting value** happen on the calling thread, so start a switch over UI elements from the UI thread. Off-thread, that write is what a WPF dependency property throws on.

⚙ **An overlapping switch cancels the one before it, cleanly.** The superseded switch stops at its next await and does **not** advance `Current` or fire `OnThemeChanged` — so a completion callback still means the values arrived. It does leave behind whatever intermediate value the properties reached. The new switch writes its configured start values back onto the targets first, so with the default `Cache` it animates from the current theme's cached value; only `Reflect` starts from the value actually on the property.

⚙ **A `null` theme value means "leave this property alone"**, not "set it to null": there is nothing to sample towards, so that property is skipped for the whole switch.

⚙ **Converters that reach application resources need them to exist** by the time `InitializeTheme()` runs — which is the same reason the call follows `InitializeComponent()`.

⚙ **Non-solid brushes are the expensive case** in an animated switch: WPF's cross-fade path reuses its bitmap, but allocates a fresh `ImageBrush` shell per frame. A solid-brush switch reuses one scratch brush with no per-frame allocation, so prefer a solid brush for a theme value that changes often.

## Reference

⚙ `Examples/Theme/WPF/Demo` and `Examples/Theme/Avalonia/Demo` — the shipped theme demos and the reference shape for everything above. They declare themeable properties with `[ThemeConfig]`, switch with animation and without it, implement `OnThemeChanging` / `OnThemeChanged`, edit a theme's values at runtime with `SetThemeValue` / `RestoreThemeValue`, and drive the timeline through pause, seek, rate and stop. Run either with `dotnet run -- bench` to repeat the same scenario headlessly over 1, 50, 200 and 1000 elements and write a timing table. `Examples/Theme/WPF Trimmed/Demo` and `Examples/Theme/Avalonia Trimmed/Demo` are the same feature reduced to one window.

⚙ `Src/Core/VeloxDev.Core/DynamicTheme/` — `ITheme`, the built-in themes, `[ThemeConfig]`, `ThemeManager` (`Current`, `SetCurrent`, `Jump`, `Transition`, `StartModel`).

⚙ `InterpolatorCore.CreateScheduler(object target, ITransitionEffectCore effect)` — the seam an adapter implements so a switch can run on that platform's scheduler. Returning null means "not animatable here", and the switch is applied at once.

⚙ `InterpolatorCore` exposes its sampler registry only through `TryGetInterpolator` / `RegisterInterpolator` / `UnregisterInterpolator`; there is no public registry property. Register a custom sampler before the switch that needs it.

⚙ `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/ThemeValueConverters.cs` — the converters a GUI supplies, and the reference for adding one for a value type your theme needs. Every adapter except **Jalium** ships this file; Jalium has the transition half of the adapter but no theme converters, so `[ThemeConfig]` has no shipped converter type to name there.
