# Pitfalls — themes

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [declaring.md](../practices/declaring.md) and [switching.md](../practices/switching.md).

## A themeable property never changes

**Symptom:** the theme switches, but one property keeps its old value.

**Cause:** the property name in `[ThemeConfig]` does not resolve, the property is not public/read-write, or the class is not `partial` / `InitializeTheme()` was not called.

**Fix:** check the spelling against the property name; make the property public with a setter; mark the class `partial`; call `InitializeTheme()` after `InitializeComponent()`. A non-public property leaves a `null` `PropertyInfo` and `InitializeTheme()` throws on it; a get-only property is silently never updated.

## The switch happens but does not animate

**Symptom:** every value changes at once, with no interpolation.

**Cause:** `ThemeManager.SetPlatformInterpolator(new Interpolator())` was not called, or the adapter's `CreateScheduler` returned null for the effect, or the effect is not the platform's `ITransitionEffect<TPriority>`.

**Fix:** call `SetPlatformInterpolator` once at startup; use `TransitionEffects.Theme` or the platform's effect type. Nothing logs when the switch degrades to instant.

## `StartModel` skips every property

**Symptom:** an animated switch updates nothing.

**Cause:** `StartModel` is set to `Reflect | Cache`, a legal `[Flags]` combination. `PrepareSamplers` switches on the exact values and matches neither arm.

**Fix:** use one value at a time (`Cache` or `Reflect`).

## A switch leaves a mixed state after `Exit`

**Symptom:** some targets show the new theme, others the old, and `Current` did not move.

**Cause:** `Transition.Exit(target)` cancelled one target's run. `ThemeManager` treats a cancelled run as "the switch did not land" and skips `Current`, `OnThemeChanged` and the held-value write.

**Fix:** let a switch finish, or start a new switch (which writes its configured start values back onto the targets). Treat `Exit` as an abort, not a clean cancel.

## A runtime value edit looks like a no-op

**Symptom:** `SetThemeValue` is called, but the property does not change.

**Cause:** the value is stored verbatim, no converter runs. Passing an array such as `new object?[] { "#fff4d6" }` stores that array as the value; the immediate `SetValue` fails silently. The array is now the override for that theme.

**Fix:** pass the type the property expects (a `Brush`, not a hex string). `RestoreThemeValue<T>` drops the whole override entry, every theme included.

## A property jumps at the end of the switch

**Symptom:** one property holds its value during the animation and snaps at the end.

**Cause:** its type has no sampler. The property is held and written at the end while its neighbours animate.

**Fix:** use a sampled type or register a sampler for it — see [converters-and-platforms.md](../practices/converters-and-platforms.md).

## The switch throws on a WPF dependency property

**Symptom:** an `InvalidOperationException` about the wrong thread, or a cross-thread access.

**Cause:** the switch was started off the UI thread. `OnThemeChanging` and the write of each property's **starting value** happen synchronously on the calling thread.

**Fix:** start the switch from the UI thread. The sampling loop itself is not pinned to a synchronization context, but the start write is.

## An overlapping switch leaves old values visible

**Symptom:** after starting a second switch quickly, a property shows an intermediate value.

**Cause:** the first switch was cancelled mid-flight and left its intermediate value. The second writes its configured start values first, so with `StartModel.Cache` it starts from the cached current-theme value; with `Reflect` it starts from the actual property.

**Fix:** use `Cache` when a clean start matters; expect `Reflect` to begin from whatever is on the property.

## A converter that reads resources returns null

**Symptom:** a theme colour does not apply, or the converter returns null.

**Cause:** application resources were not populated when `InitializeTheme()` ran, or the resource key is missing.

**Fix:** ensure the application resources exist before the window's `InitializeTheme()`; verify the key. A `null` value means "leave this property alone" for the switch.
