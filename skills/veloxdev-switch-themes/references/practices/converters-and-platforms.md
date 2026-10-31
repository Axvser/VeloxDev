# Converters and platform seams

## The converter sets

Each adapter ships its own converters; there is no shared set. Naming a converter your adapter does not ship is a compile error, not a runtime miss.

| Adapter | Converters |
|---|---|
| WPF, Avalonia, WinUI, MAUI | `BrushConverter`, `DoubleConverter`, `PointConverter`, `ThicknessConverter`, `CornerRadiusConverter`, `ColorConverter`, `ObjectConverter` |
| WinForms | `DoubleConverter`, `IntConverter`, `FloatConverter`, `PointConverter`, `PointFConverter`, `SizeConverter`, `SizeFConverter`, `RectangleConverter`, `RectangleFConverter`, `PaddingConverter`, `ColorConverter`, `FontConverter`, `ObjectConverter` |
| Razor | `DoubleConverter`, `StringConverter`, `IntConverter`, `BoolConverter` |
| Jalium | none — write an `IThemeValueConverter` |

⚙ **WinForms has no `BrushConverter`.** Its `ObjectConverter` builds a `SolidBrush` from a hex string when the target type is a `Brush`.

⚙ **`IThemeValueConverter` is the seam for a value type your theme needs.** It receives the target type, the property name and the attribute arguments, and returns the value. The generator instantiates it with `Activator.CreateInstance`, so it needs a public parameterless constructor.

## The platform seam

`InterpolatorCore.CreateScheduler(object target, ITransitionEffectCore effect)` is the seam an adapter implements so a switch can run on that platform's scheduler. Returning null means "not animatable here", and the switch is applied at once.

⚙ **The priority type is the framework's.** `DispatcherPriority` for WPF, Avalonia and Jalium; `DispatcherQueuePriority` for WinUI; `NonPriority` for MAUI, WinForms and Razor. The type is used in two places — the scheduler's type argument and the type `CreateScheduler` tests the effect against — and they must agree.

⚙ **Register a custom sampler before the switch that needs it.** `InterpolatorCore` exposes its registry only through `TryGetInterpolator` / `RegisterInterpolator` / `UnregisterInterpolator`; there is no public registry property.

⚙ **A theme value whose type has no sampler is held, then written at the end.** It does not fail the switch; its neighbours animate normally.

## Platform notes

⚙ **Jalium has the transition half of the adapter but no `ThemeValueConverters.cs`.** A Jalium project writes its own converter, or uses a value type Core already samples.

⚙ **Converters that reach application resources need them to exist** by the time `InitializeTheme()` runs. WPF's converter falls back to `Application.Current.Resources` / `TryFindResource`; Avalonia's to `Application.Current.TryFindResource`. Both are populated at start-up, before the window exists. That is a separate requirement from the `InitializeComponent()` ordering.

⚙ **Non-solid brushes are the expensive case** in an animated switch: WPF's cross-fade path reuses its bitmap but allocates a fresh `ImageBrush` shell per frame. A solid-brush switch reuses one scratch brush with no per-frame allocation; prefer a solid brush for a value that changes often.
