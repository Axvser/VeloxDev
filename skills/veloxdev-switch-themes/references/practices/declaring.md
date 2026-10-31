# Declaring a theme and its properties

## A theme is a marker

```csharp
public class Light : ITheme { }
```

That is the whole class — `Dark` and `Light` ship with Core, and a custom theme is written the same way. **The values live in the attributes that name the theme**, not on the theme object.

## `[ThemeConfig]`

The attribute goes on the **class**, once per property, and names the property as a string:

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

⚙ **The type parameters are one converter followed by the themes.** The constructor arguments are the property name followed by **one argument array per theme, in the same order**. Position is the pairing; a theme listed second takes the second array.

⚙ **At least two themes, and at most six.** The attribute family declares arities up to seven themes; the generator discovers the two-to-six range. A class whose only rows are seven-theme rows generates nothing, silently — keep the rows at six or fewer themes.

⚙ **The converter is instantiated by the generator** (`Activator.CreateInstance`), so it needs a **public parameterless constructor**. It does not have to come from the adapter: the attribute constrains it to `class, IThemeValueConverter`, so an adapter converter or your own public one both work.

⚙ **The property is matched by name at build time.** A name that does not resolve is dropped without a diagnostic. It is a **public, read/write instance property**: the generated registration uses `typeof(YourClass).GetProperty(nameof(P))`, and the write is `PropertyInfo.SetValue`. A get-only property is never updated; a non-public one leaves a `null` `PropertyInfo` and `InitializeTheme()` throws on it.

⚙ **Put the theme declarations in their own `partial` class block.** They are configuration, not interaction logic, and keeping them apart is what the shipped demo does.

## Turning it on

```csharp
private void LoadTheme()
{
    InitializeTheme();   // required, and after InitializeComponent()

    // Required for an animated switch; without it a switch still happens, immediately.
    ThemeManager.SetPlatformInterpolator(new Interpolator());

    // Where an animated switch takes its starting value from.
    ThemeManager.StartModel = StartModel.Cache;
}
```

⚙ **`InitializeTheme()` is generated and is called once.** It registers the object, applies the current theme's values to its properties, and registers the declared values for its type. `Register` is not called by hand; `Unregister` is the explicit way to stop tracking an instance before it is collected.

⚙ **It comes after `InitializeComponent()`.** It writes the current theme's values onto the properties; if the tree does not exist yet, the XAML build that follows applies its own default values and overwrites that write.

⚙ **The class is `partial`.** A non-partial class gets no generated code — and no error.

## Which converter

| GUI | Converters shipped |
|---|---|
| WPF, Avalonia, WinUI, MAUI | `BrushConverter` plus double / point / thickness / corner-radius / color / object |
| WinForms | `Double`, `Int`, `Float`, `Point`, `PointF`, `Size`, `SizeF`, `Rectangle`, `RectangleF`, `Padding`, `Color`, `Font`, `Object` — no `BrushConverter`; `ObjectConverter` makes a `SolidBrush` from a hex string |
| Razor | `Double`, `String`, `Int`, `Bool` |
| Jalium | no `ThemeValueConverters.cs`; write your own `IThemeValueConverter` |

⚙ **Each adapter ships its own converter set.** Naming a converter your adapter does not ship is a compile error, not a runtime miss. See [converters-and-platforms.md](converters-and-platforms.md).
