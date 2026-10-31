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
| one GUI adapter (`VeloxDev.WPF`, `VeloxDev.Avalonia`, `VeloxDev.WinUI`, `VeloxDev.MAUI`, `VeloxDev.WinForms`, `VeloxDev.Razor`, `VeloxDev.Jalium`) | the **converters** that turn a theme's parameters into that framework's value type, and — transitively — `VeloxDev.Core` |
| `VeloxDev.Core` | `ITheme`, the theme classes, `[ThemeConfig]`, `ThemeManager`, and the generator |

The adapter brings `VeloxDev.Core` in transitively, so one reference is enough. Add Core explicitly when you also want its non-UI parts.

Core cannot do it alone: a theme's parameters are plain values (a hex string), and only the adapter knows how to make a `System.Windows.Media.Brush` or an `Avalonia.Media.IBrush` out of one. The adapter also supplies the `Interpolator` you register for animated switching — and, through it, the scheduler a switch runs on.

## The shortest working declaration

```csharp
public class Light : ITheme { }

[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InitializeTheme();
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        ThemeManager.StartModel = StartModel.Cache;
    }
}
```

⚙ **A theme class is an empty marker.** The values live in the attributes that name the theme, not on the theme object. `Dark` and `Light` ship with Core; a custom theme is written the same way.

⚙ **The attribute goes on the class, once per property, and names the property as a string.** The type parameters are one converter followed by the themes; the constructor arguments are the property name followed by one argument array per theme, in the same order.

⚙ **The class is `partial` and `InitializeTheme()` is called after `InitializeComponent()`.**

## Where to read next

| You are about to… | Read |
|---|---|
| Declare a theme and its properties, pick a converter | [references/practices/declaring.md](references/practices/declaring.md) |
| Switch instantly or with animation, control a switch in flight | [references/practices/switching.md](references/practices/switching.md) |
| React to a theme change, edit a theme's values at runtime | [references/practices/switching.md](references/practices/switching.md#reacting-to-a-change) |
| Check which converters a platform ships, or add your own | [references/practices/converters-and-platforms.md](references/practices/converters-and-platforms.md) |

## Pitfalls

| Symptom | Read |
|---|---|
| A themeable property never changes | [references/pitfalls/theme.md](references/pitfalls/theme.md) |
| The switch happens but does not animate | [references/pitfalls/theme.md](references/pitfalls/theme.md) |
| A switch leaves a mixed state, or a property jumps at the end | [references/pitfalls/theme.md](references/pitfalls/theme.md) |
| A runtime value edit looks like a no-op | [references/pitfalls/theme.md](references/pitfalls/theme.md) |
| The switch throws on the UI thread, or the camera is left mid-transition | [references/pitfalls/theme.md](references/pitfalls/theme.md) |

## Reference

⚙ `Examples/Theme/WPF/Demo` and `Examples/Theme/Avalonia/Demo` — the shipped theme demos and the reference shape. They declare themeable properties, switch with and without animation, implement `OnThemeChanging` / `OnThemeChanged`, edit values with `SetThemeValue` / `RestoreThemeValue`, and drive the timeline through pause, seek, rate and stop. Run either with `dotnet run -- bench` to repeat the same scenario headlessly over 1, 50, 200 and 1000 elements. `Examples/Theme/WPF Trimmed/Demo` and `Examples/Theme/Avalonia Trimmed/Demo` are the reduced one-window versions.

⚙ `Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/` — `ITheme`, `IThemeObject`, `IThemeValueConverter`. The implementations are next door in `Src/Core/VeloxDev.Core/DynamicTheme/` — `Dark`, `Light`, `ThemeCache`, `[ThemeConfig]` (`ThemeConfigAttribute.cs`) and `ThemeManager`.

⚙ `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/ThemeValueConverters.cs` — the converters a GUI supplies. Jalium has the transition half of the adapter but no theme converters.
