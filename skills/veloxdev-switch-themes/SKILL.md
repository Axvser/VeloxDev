---
name: veloxdev-switch-themes
description: Give a VeloxDev app runtime themes that can animate when they change — declare a theme class, name its themeable properties in [ThemeConfig] on the class, call InitializeTheme(), and switch with SetCurrent, Jump or Transition. Use when adding runtime themes or animated theme switching.
---

## Responsibility

Let a property's value depend on which theme is current, and switch themes at runtime — instantly or animated — without writing per-theme branches anywhere in the view.

A themeable property is declared by naming it in an attribute **on the class**, together with the value it takes in each theme. The generator wires the rest: registration, applying the current theme at start-up, and the machinery a switch drives.

## Which packages

One adapter reference, and Core comes with it:

| | provides |
|---|---|
| one GUI adapter (`VeloxDev.WPF`, `VeloxDev.Avalonia`, `VeloxDev.WinUI`, `VeloxDev.MAUI`, `VeloxDev.WinForms`, `VeloxDev.Razor`) | the **converters** that turn a theme's parameters into that framework's value type, and — transitively — `VeloxDev.Core` |
| `VeloxDev.Core` | `ITheme`, the theme classes, `[ThemeConfig]`, `ThemeManager`, and the generator |

The adapter brings `VeloxDev.Core` in transitively, so one reference is enough. Add Core explicitly only if you also want its non-UI parts.

Core cannot do it alone: a theme's parameters are plain values (a hex string), and only the adapter knows how to make a `System.Windows.Media.Brush` or an `Avalonia.Media.IBrush` out of one. It is also what supplies the `Interpolator` you register for animated switching — and, through it, the scheduler a switch runs on.

⚙ **Each adapter ships its own converter set — there is no shared one.** WPF, Avalonia, WinUI and MAUI ship `BrushConverter` plus double / point / thickness / corner-radius / color / object converters. WinForms ships `Double`, `Int`, `Float`, `Point`, `PointF`, `Size`, `SizeF`, `Rectangle`, `RectangleF`, `Padding`, `Color`, `Font` and `Object` — and **no `BrushConverter`** (there its `ObjectConverter` is what makes a `SolidBrush` from a hex string). Razor ships only `Double`, `String`, `Int` and `Bool`. Naming a converter your adapter does not ship is a compile error, not a runtime miss.

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

⚙ **The sample above is WPF / Avalonia / WinUI / MAUI only.** `BrushConverter` exists in those four adapters; WinForms and Razor do not ship it, so that attribute line is a CS0246 there. Name one of that adapter's own converters instead (see the converter sets under *Which packages*).

⚙ The type parameters are **one converter followed by the themes**; the constructor arguments are the property name followed by **one argument array per theme, in the same order**. Position is the pairing — a theme listed second takes the second array, so swapping two theme type arguments silently swaps their values.

⚙ **At least two themes, and at most six.** The attribute family declares arities up to seven themes, but the generator only *discovers* classes that carry a two-to-six-theme row. A seven-theme row is emitted only when the same class also has a row in that range; a class whose only rows are seven-theme rows compiles and then **generates nothing at all**, silently, exactly like a missing `partial`. Stay at six or fewer.

⚙ The converter is **instantiated by the generator** (`Activator.CreateInstance`), so it needs a **public parameterless constructor**. It does not have to come from the adapter: the attribute only constrains it to `class, IThemeValueConverter`, so an adapter converter or your own public one both work.

⚙ The property is matched **by name at build time**. A name that does not resolve is dropped without a diagnostic — if a property never changes with the theme, check the spelling before anything else. It must also be a **public, read/write instance property**: the generated registration is `typeof(YourClass).GetProperty(nameof(P))`, which never returns a non-public member, and the write is `PropertyInfo.SetValue`. A get-only property is silently never updated; a non-public one leaves a `null` `PropertyInfo` in the static cache, and `InitializeTheme()` throws on it.

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

⚙ **It must come after `InitializeComponent()`.** It writes the current theme's values onto the properties; if you call it before the tree exists, the XAML build that follows applies its own default values and overwrites that write. The ordering is about the tree, not about resource lookup — converters that read application resources are a separate caveat, below.

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

⚙ `Transition<T>` is a plain `void` wrapper around the `async void` `Transition(Type, ITransitionEffectCore)`: it returns after preparing the switch, and `Current` plus `OnThemeChanged` move only when the animation actually lands. There is nothing to await. An exception from the run itself is logged, not thrown. The two hooks are **not treated alike**: `OnThemeChanging` runs synchronously inside the call, before the first await, while `OnThemeChanged` runs in the continuation after the run lands. Neither sits inside the run's `try`, and because the entry point is `async void`, neither exception can reach a caller — both surface as unhandled async-void exceptions, at different times.

⚙ `TransitionEffects.Theme` is the adapter's ready-made effect for this; reach for it rather than building one, and swap in your own `TransitionEffect` only when you want a different duration or curve. The scheduler accepts any `ITransitionEffect<TPriority>` for its framework — `DispatcherPriority` for WPF, Avalonia and Jalium, `DispatcherQueuePriority` for WinUI, `NonPriority` for MAUI, WinForms and Razor. A foreign effect makes `CreateScheduler` return null and the whole switch fall back to an instant apply.

⚙ **The effect's other settings are honoured**, not just `Duration` and `Ease`: `FPS` caps the sample rate, `IsAutoReverse` adds a reverse pass, and `LoopTime` adds that many extra iterations of the run (0 is one iteration). A switch with `LoopTime = 1` and `IsAutoReverse = true` therefore settles back on the **start** value — that is the flags working, not a bug.

⚙ **The effect is read by every target, every frame, for the whole switch.** Pass a `TransitionEffects` instance or a fresh `TransitionEffect`, and do not mutate or subscribe to one while a switch that uses it is in flight — a handler on `TransitionEffects.Theme` fires once per target per frame.

## Controlling a switch in flight

One switch drives every target that contributes at least one eligible property through a **single shared timeline** — a target that resolves no property is skipped, not animated. The control plane the transition system already exposes therefore reaches the switch unchanged: `Pause`, `Resume` and `SetRate` on any one target move the whole switch, while the rest act on the target you pass.

```csharp
Transition.Pause(mainWindow);                       // freeze the whole switch
Transition.Resume(mainWindow);
Transition.SetRate(mainWindow, 0.5);                // half speed; 0 freezes the clock, IsPaused stays false
Transition.Seek(mainWindow, TimeSpan.FromSeconds(2)); // re-anchor this target only, keeping the rate
Transition.IsPaused(mainWindow);                    // true only when every run on the target is paused
Transition.Rate(mainWindow);                        // the configured rate
Transition.Cycle(mainWindow);                       // the pass index of this target's first run
Transition.Position(mainWindow);                    // how far into the current pass
Transition.Exit(mainWindow);                        // cancel this target's runs; the others keep going
```

⚙ Only `Pause`, `Resume` and `SetRate` are switch-wide: they act on the shared timeline, so any target that has a run reaches all of them. `Seek`, `Cycle`, `Position` and `Exit` are **per-target**. `Seek` re-anchors only the run(s) on the target you pass — the other targets keep their own anchors and carry on — and `Cycle` / `Position` read the first run on that target. This is deliberate: anchor and pass counter are per-run, which is what lets several animations share one timeline at all.

⚙ These statics are declared on `TransitionCore` (`VeloxDev.TransitionSystem.Abstractions`); the adapter's non-generic `Transition` inherits them, so there is nothing theme-specific to call.

⚙ `Exit(target)` is **not a switch-wide stop.** It cancels only the runs registered under that target; every other target keeps animating to its endpoint. `ThemeManager` then sees one cancelled run among the switch's runs, concludes the switch did not land, and skips `Current`, `OnThemeChanged` and the end-value write for properties no sampler claimed — so the old theme stays current while the targets that did finish show the new values. Treat it as an abort that leaves a mixed state, not as a clean cancel; the next animated switch writes its configured start values back onto the targets before animating.

⚙ A pause costs **no timer wake-ups** — the sampling loop parks on a signal — and the paused interval is excluded from the animation rather than merely skipped, so a pause of any length leaves the remaining duration unchanged.

⚙ Seeking past the end of a pass **finishes** it, landing exactly on the endpoint, rather than being clamped.

## Reacting to a change

⚙ **There is no event to subscribe to.** The generator declares two partial methods on your class; implement them and they are called:

```csharp
partial void OnThemeChanged(Type? oldValue, Type? newValue) { … }
partial void OnThemeChanging(Type? oldValue, Type? newValue) { … }
```

⚙ `OnThemeChanging` runs before any value moves. `OnThemeChanged` runs only for a switch that lands — a cancelled, superseded or `Exit`-ed switch announces nothing. They are **not a symmetric pair**: changing has already fired when that switch started, so a later cancel leaves it unmatched. Read Changed as "the new theme landed", never as cleanup that is guaranteed to run.

## Editing a theme's values at runtime

The generator also emits a set of accessors on the class:

```csharp
SetThemeValue<Light>(nameof(Background), new SolidColorBrush(Colors.White)); // store a value for Light
RestoreThemeValue<Light>(nameof(Foreground));                                // drop the override

var declared = GetStaticThemeCache();   // what the attributes declared
var overrides = GetActiveThemeCache();  // what has been changed at runtime
```

⚙ **The value is stored verbatim — no converter runs.** `SetThemeValue<T>` is generic only to name the theme; its `newValue` is `object?`. Pass the type the property itself expects (for a WPF `Background`, a `Brush`). A raw `[ThemeConfig]` context array such as `new object?[] { "#fff4d6" }` is stored as data, and the immediate re-apply swallows the failed `SetValue` — so the edit looks like a no-op. It is not one: the array stays in the active cache as that theme's value, and the next switch to that theme uses it as the end value. WPF's brush sampler substitutes `Brushes.Transparent` for an end that is not a `Brush`, so a solid-brush start cross-fades to transparent. (The shipped demo does pass an array.)

⚙ `RestoreThemeValue<T>` removes the property's whole override entry — every theme, not only `T` — then re-applies the value for the current theme.

⚙ **An override wins over the declared value**, and only the properties you changed appear in the active cache. An override stored for a non-current theme takes effect on the next switch to that theme.

⚙ Both caches are keyed `property name → property → theme → value`, so they can be walked for anything the built-in accessors do not cover.

## Pitfalls

⚙ **Switch on the UI thread.** Frames are marshalled through the adapter's `UIThreadInspector` — WPF's `ThreadFor` resolves a `DispatcherObject` target to that object's own `Dispatcher`, and `PostCore` posts each write through `Dispatcher.InvokeAsync`. The sampling loop itself is **not** pinned to a `SynchronizationContext`; it can resume on a pool thread, and the effect's `Update` / `LateUpdate` callbacks run on whatever thread it resumed on. What is not marshalled is the synchronous start of the switch: `OnThemeChanging` and the write of each property's **starting value** happen on the calling thread, so start a switch over UI elements from the UI thread. Off-thread, that write is what a WPF dependency property throws on.

⚙ **An overlapping switch cancels the one before it, cleanly.** The superseded switch stops at its next await and does **not** advance `Current` or fire `OnThemeChanged` — so a completion callback still means the values arrived. It does leave behind whatever intermediate value the properties reached. The new switch writes its configured start values back onto the targets first, so with the default `Cache` it animates from the current theme's cached value; only `Reflect` starts from the value actually on the property.

⚙ **A `null` theme value means "leave this property alone"**, not "set it to null": there is nothing to sample towards, so that property is skipped for the whole switch.

⚙ **Converters that reach application resources need them to exist** by the time `InitializeTheme()` runs. WPF's converter falls back to `Application.Current.Resources` / `TryFindResource`, Avalonia's to `Application.Current.TryFindResource`; both are populated at start-up, before the window exists. This is a separate requirement from the `InitializeComponent()` ordering above, not the reason for it.

⚙ **Non-solid brushes are the expensive case** in an animated switch: WPF's cross-fade path reuses its bitmap, but allocates a fresh `ImageBrush` shell per frame. A solid-brush switch reuses one scratch brush with no per-frame allocation, so prefer a solid brush for a theme value that changes often.

## Reference

⚙ `Examples/Theme/WPF/Demo` and `Examples/Theme/Avalonia/Demo` — the shipped theme demos and the reference shape for everything above. They declare themeable properties with `[ThemeConfig]`, switch with animation and without it, implement `OnThemeChanging` / `OnThemeChanged`, edit a theme's values at runtime with `SetThemeValue` / `RestoreThemeValue`, and drive the timeline through pause, seek, rate and stop. Run either with `dotnet run -- bench` to repeat the same scenario headlessly over 1, 50, 200 and 1000 elements and write a timing table. `Examples/Theme/WPF Trimmed/Demo` and `Examples/Theme/Avalonia Trimmed/Demo` are the same feature reduced to one window.

⚙ `Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/` — `ITheme`, `IThemeObject`, `IThemeValueConverter`. The implementations are next door in `Src/Core/VeloxDev.Core/DynamicTheme/` — `Dark`, `Light`, `ThemeCache`, `[ThemeConfig]` (`ThemeConfigAttribute.cs`) and `ThemeManager` (`Current`, `SetCurrent`, `Jump`, `Transition`, `StartModel`).

⚙ `InterpolatorCore.CreateScheduler(object target, ITransitionEffectCore effect)` — the seam an adapter implements so a switch can run on that platform's scheduler. Returning null means "not animatable here", and the switch is applied at once.

⚙ `InterpolatorCore` exposes its sampler registry only through `TryGetInterpolator` / `RegisterInterpolator` / `UnregisterInterpolator`; there is no public registry property. Register a custom sampler before the switch that needs it.

⚙ `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/ThemeValueConverters.cs` — the converters a GUI supplies (each adapter's set differs; see *Which packages*), and the reference for adding one for a value type your theme needs. Every adapter except **Jalium** ships this file; Jalium has the transition half of the adapter but no theme converters, so `[ThemeConfig]` has no shipped converter type to name there.
