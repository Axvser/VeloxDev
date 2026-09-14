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
| one GUI adapter (`VeloxDev.WPF`, `VeloxDev.Avalonia`, …) | the **converters** that turn a theme's parameters into that framework's value type — `BrushConverter` and friends |

Core cannot do it alone: a theme's parameters are plain values (a hex string), and only the adapter knows how to make a `System.Windows.Media.Brush` or an `Avalonia.Media.IBrush` out of one. It is also what supplies the `Interpolator` you register for animated switching — and, through it, the scheduler a switch runs on.

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

⚙ **At least one converter and two themes, and at most six.** The attribute itself declares arities all the way up to seven themes — but the generator only registers the two-to-six-theme forms, so **a seven-theme declaration compiles and then generates nothing at all**, silently, exactly like a missing `partial`. Stay at six or fewer.

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

⚙ **`InitializeTheme()` is generated and must be called.** It registers the object, applies the current theme's values to its properties, and registers the declared values for its type. You never call `Register` / `Unregister` yourself.

⚙ **It must come after `InitializeComponent()`.** Converters look up application resources, and those are not populated until the view is built.

⚙ **The class must be `partial`.** A non-partial class gets no generated code at all — and no error, so the theme simply never works.

⚙ `SetPlatformInterpolator` is **mandatory for animated switching**. It is what lets the switch run on the adapter's `TransitionSchedulerCore` — the same one a `Transition<T>` run uses. Without it the switch is not animated **at all**: every value is written at once, the same as `Jump`. Nothing warns, so a switch that looks like "the theme has no animation" is usually this line.

⚙ A property whose type has **no sampler** is a separate, narrower case: that one property keeps its old value for the whole switch and jumps at the end, while its neighbours animate normally.

⚙ `StartModel` picks where an animated switch starts from: `Cache` (the value the theme system last knew) or `Reflect` (read the live property). Both settings are global.

## Switching

```csharp
ThemeManager.SetCurrent<Light>();                       // switch now
ThemeManager.Jump<Light>();                             // switch now, without animating
ThemeManager.Transition<Light>(TransitionEffects.Theme); // switch, animating the change

var current = ThemeManager.Current;                     // a Type
```

⚙ `Current` is a `Type`, not an instance — compare with `ThemeManager.Current == typeof(Dark)`.

⚙ `TransitionEffects.Theme` is the adapter's ready-made effect for this; reach for it rather than building one, and swap in your own `TransitionEffect` only when you want a different duration or curve.

⚙ **The effect's other settings are honoured**, not just `Duration` and `Ease`: `FPS` caps the sample rate, `IsAutoReverse` adds a reverse pass, and `LoopTime` repeats the whole thing. A switch with `LoopTime = 1` and `IsAutoReverse = true` therefore settles back on the **start** value — that is the flags working, not a bug.

⚙ **The effect is read by every target, every frame, for the whole switch.** Pass a `TransitionEffects` instance or a fresh `TransitionEffect`, and do not mutate or subscribe to one while a switch that uses it is in flight — a handler on `TransitionEffects.Theme` fires once per target per frame.

## Controlling a switch in flight

One switch drives every registered target through a **single shared timeline**. So the timeline control the transition system already exposes reaches it unchanged — call it on any one of the targets and the whole switch moves, because there is only one transport to move:

```csharp
Transition.Pause(mainWindow);                       // freeze the whole switch
Transition.Resume(mainWindow);
Transition.SetRate(mainWindow, 0.5);                // half speed; 0 also pauses
Transition.Seek(mainWindow, TimeSpan.FromSeconds(2)); // jump to 2s in, keeping the rate
Transition.IsPaused(mainWindow);                    // true only when every run on the target is paused
Transition.Position(mainWindow);                    // how far into the current pass
Transition.Exit(mainWindow);                        // stop it outright, leaving values where they are
```

⚙ `Transition` here is the adapter's non-generic class; the same methods are also on `TransitionCore`, which is what a Core-only test would call.

⚙ A pause costs **no timer wake-ups** — the sampling loop parks on a signal — and the paused interval is excluded from the animation rather than merely skipped, so a pause of any length leaves the remaining duration unchanged.

⚙ Seeking past the end of a pass **finishes** it, landing exactly on the endpoint, rather than being clamped.

## Reacting to a change

⚙ **There is no event to subscribe to.** The generator declares two partial methods on your class; implement them and they are called:

```csharp
partial void OnThemeChanged(Type? oldValue, Type? newValue) { … }
partial void OnThemeChanging(Type? oldValue, Type? newValue) { … }
```

## Editing a theme's values at runtime

The generator also emits a set of accessors on the class:

```csharp
SetThemeValue<Light>(nameof(Background), new object?[] { "#ffffff" });   // override one value for one theme
RestoreThemeValue<Light>(nameof(Foreground));                           // put it back

var declared = GetStaticThemeCache();   // what the attributes declared
var overrides = GetActiveThemeCache();  // what has been changed at runtime
```

⚙ **The value array is written the same way the attribute writes it, but not in the same syntax.** In the attribute a collection expression works — `["#ffffff"]` — because the constructor takes `params object?[]`. `SetThemeValue`'s second parameter is a plain `object?`, so there a collection expression **does not compile** (`CS9174`): spell it `new object?[] { "#ffffff" }`.

⚙ **An override wins over the declared value**, and only the properties you changed appear in the active cache.

⚙ Both caches are keyed `property name → property → theme → value`, so they can be walked for anything the built-in accessors do not cover.

## Pitfalls

⚙ **Switch on the UI thread.** The frames are marshalled for you, but two things are not: `OnThemeChanging` and the write of each property's **starting value** both happen on the calling thread, before the switch starts. Off-thread, that write is what a WPF dependency property throws on.

⚙ **An overlapping switch cancels the one before it, cleanly.** The superseded switch stops at its next frame and does **not** advance `Current` or fire `OnThemeChanged` — so a completion callback still means the values arrived. What it does leave behind is whatever intermediate value the properties had reached when it stopped; the new switch animates each property from where it actually is.

⚙ **A `null` theme value means "leave this property alone"**, not "set it to null": there is nothing to sample towards, so that property is skipped for the whole switch.

⚙ **Converters that reach application resources need them to exist** by the time `InitializeTheme()` runs — which is the same reason the call follows `InitializeComponent()`.

⚙ **Non-solid brushes are the expensive case** in an animated switch: a sampler that cannot reuse a scratch brush allocates a fresh one per frame. Prefer a solid brush for a theme value that changes often.

## Reference

⚙ `Examples/Theme/<GUI>/Demo` — the shipped theme demo, in WPF and Avalonia, and the reference shape for everything above. It declares themeable properties with `[ThemeConfig]`, switches with animation and without it, implements `OnThemeChanging` / `OnThemeChanged`, edits a theme's values at runtime with `SetThemeValue` / `RestoreThemeValue`, and drives the timeline through pause, seek, rate and stop. Run it with `bench` to repeat the same scenario headlessly over 1…1000 elements and write a timing table.

⚙ `Src/Core/VeloxDev.Core/DynamicTheme/` — `ITheme`, the built-in themes, `[ThemeConfig]`, `ThemeManager` (`Current`, `SetCurrent`, `Jump`, `Transition`, `StartModel`).

⚙ `InterpolatorCore.CreateScheduler(object target, ITransitionEffectCore effect)` — the seam an adapter implements so a switch can run on that platform's scheduler. Returning null means "not animatable here", and the switch is applied at once.

⚙ `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/ThemeValueConverters.cs` — the converters a GUI supplies, and the reference for adding one for a value type your theme needs. Every adapter except **Jalium** ships this file; Jalium has the transition half of the adapter but no theme converters, so `[ThemeConfig]` has no converter type to name there.
