# Switching, controlling and reacting

## The three switches

```csharp
ThemeManager.SetCurrent<Light>();                        // set Current only: no values, no hooks
ThemeManager.Jump<Light>();                              // switch now, synchronously, without animating
ThemeManager.Transition<Light>(TransitionEffects.Theme);  // start an animated switch

var current = ThemeManager.Current;                      // a Type; defaults to typeof(Dark)
```

⚙ **`Current` is a `Type`, not an instance.** Compare with `ThemeManager.Current == typeof(Dark)`. `SetCurrent<T>` changes only that property; it seeds the starting theme before elements register.

⚙ **`Transition<T>` is a plain `void` wrapper around the `async void` `Transition(Type, ITransitionEffectCore)`.** It returns after preparing the switch, and `Current` plus `OnThemeChanged` move when the animation lands. There is nothing to await.

⚙ **Transitions and jumps to the theme already current are ignored**, with a `Debug.WriteLine`.

⚙ **An overlapping switch cancels the one before it, cleanly.** The superseded switch stops at its next await and does not advance `Current` or fire `OnThemeChanged`. It leaves behind whatever intermediate value the properties reached; the new switch writes its configured start values back onto the targets first.

## Animated switching

⚙ **`SetPlatformInterpolator` is mandatory for animated switching.** It is what lets the switch run on the adapter's `TransitionSchedulerCore` — the same one a `Transition<T>` run uses. Without it the switch is not animated: every value is written at once, the same as `Jump`. A switch that looks like "the theme has no animation" usually has this line missing.

⚙ **A property whose type has no sampler** keeps its old value for the whole switch and jumps at the end, while its neighbours animate normally.

⚙ **`StartModel` picks where an animated switch starts from:** `Cache` (the cached value for the current theme — the instance override first, then the declared value) or `Reflect` (read the live property). Both settings are global.

⚙ **Use one `StartModel` value.** `StartModel.Reflect | StartModel.Cache` is a legal `[Flags]` value but not a usable mode: `PrepareSamplers` switches on the exact values, matches neither arm, and skips every property.

⚙ **`TransitionEffects.Theme` is the adapter's ready-made effect.** Reach for it rather than building one, and swap in your own `TransitionEffect` only for a different duration or curve. The scheduler accepts any `ITransitionEffect<TPriority>` for its framework — `DispatcherPriority` for WPF, Avalonia and Jalium, `DispatcherQueuePriority` for WinUI, `NonPriority` for MAUI, WinForms and Razor. A foreign effect makes `CreateScheduler` return null and the whole switch falls back to an instant apply.

⚙ **The effect's other settings are honoured**, not just `Duration` and `Ease`: `FPS` caps the sample rate, `IsAutoReverse` adds a reverse pass, and `LoopTime` adds that many extra iterations (0 is one iteration). A switch with `LoopTime = 1` and `IsAutoReverse = true` settles back on the **start** value — that is the flags working.

⚙ **The effect is read by every target, every frame, for the whole switch.** Pass a `TransitionEffects` instance or a fresh `TransitionEffect`, and do not mutate or subscribe to one while a switch that uses it is in flight.

## Controlling a switch in flight

One switch drives every target that contributes at least one eligible property through a **single shared timeline** — a target that resolves no property is skipped.

```csharp
Transition.Pause(mainWindow);                        // freeze the whole switch
Transition.Resume(mainWindow);
Transition.SetRate(mainWindow, 0.5);                 // half speed; 0 freezes the clock, IsPaused stays false
Transition.Seek(mainWindow, TimeSpan.FromSeconds(2));// re-anchor this target only, keeping the rate
Transition.IsPaused(mainWindow);                     // true only when every run on the target is paused
Transition.Rate(mainWindow);                         // the configured rate
Transition.Cycle(mainWindow);                        // the pass index of this target's first run
Transition.Position(mainWindow);                     // how far into the current pass
Transition.Exit(mainWindow);                         // cancel this target's runs; the others keep going
```

⚙ **Only `Pause`, `Resume` and `SetRate` are switch-wide.** They act on the shared timeline, so any target that has a run reaches all of them. `Seek`, `Cycle`, `Position` and `Exit` are **per-target**: `Seek` re-anchors only the run(s) on the target you pass, and `Cycle` / `Position` read the first run on that target.

⚙ These statics are declared on `TransitionCore` (`VeloxDev.TransitionSystem.Abstractions`); the adapter's non-generic `Transition` inherits them.

⚙ `Exit(target)` is **not a switch-wide stop.** It cancels only the runs registered under that target. `ThemeManager` sees one cancelled run among the switch's runs, concludes the switch did not land, and skips `Current`, `OnThemeChanged` and the end-value write for properties no sampler claimed — so the old theme stays current while the targets that did finish show the new values. Treat it as an abort that leaves a mixed state; the next animated switch writes its configured start values back onto the targets.

⚙ A pause costs **no timer wake-ups** — the sampling loop parks on a signal — and the paused interval is excluded from the animation. Seeking past the end of a pass finishes it at the endpoint.

## Reacting to a change

⚙ **The generator declares two partial methods on your class.** Implement them and they are called:

```csharp
partial void OnThemeChanged(Type? oldValue, Type? newValue) { … }
partial void OnThemeChanging(Type? oldValue, Type? newValue) { … }
```

⚙ `OnThemeChanging` runs synchronously inside the call, before any value moves. `OnThemeChanged` runs only for a switch that lands — a cancelled, superseded or `Exit`-ed switch announces nothing. They are not a symmetric pair: a later cancel leaves `Changing` unmatched. Read `Changed` as "the new theme landed".

## Editing a theme's values at runtime

```csharp
SetThemeValue<Light>(nameof(Background), new SolidColorBrush(Colors.White)); // store a value for Light
RestoreThemeValue<Light>(nameof(Foreground));                                // drop the override

var declared = GetStaticThemeCache();   // what the attributes declared
var overrides = GetActiveThemeCache();  // what has been changed at runtime
```

⚙ **The value is stored verbatim — no converter runs.** `SetThemeValue<T>` is generic only to name the theme; its `newValue` is `object?`. Pass the type the property itself expects (for a WPF `Background`, a `Brush`). A raw `[ThemeConfig]` context array such as `new object?[] { "#fff4d6" }` is stored as data, and the immediate re-apply swallows the failed `SetValue` — so the edit looks like a no-op. It is not: the array stays in the active cache as that theme's value, and the next switch to that theme uses it as the end value. WPF's brush sampler substitutes `Brushes.Transparent` for an end that is not a `Brush`, so a solid-brush start cross-fades to transparent.

⚙ `RestoreThemeValue<T>` removes the property's whole override entry — every theme, not only `T` — then re-applies the value for the current theme.

⚙ **An override wins over the declared value**, and only the properties you changed appear in the active cache. An override stored for a non-current theme takes effect on the next switch to that theme.

⚙ Both caches are keyed `property name → property → theme → value`, so they can be walked for anything the built-in accessors do not cover.
