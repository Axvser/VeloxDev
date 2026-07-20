# Theme

This is a branch capability extended from the animation system, allowing a natural transition from one theme color scheme to another within the UI.

> **Configuration**

```csharp
// [ Global Effect ]
// Register the interpolator provided by the platform adaptation package ( e.g., VeloxDev.WPF) for the theme system
ThemeManager.SetPlatformInterpolator(new Interpolator());

// [ Global Effect ]
// When the theme changes, do you want the animation's starting state to be taken from the cache, or to use the current state obtained via reflection as the starting point?
ThemeManager.StartModel = StartModel.Cache;
```

> **Build**

```csharp
// At most 7 themes can be configured simultaneously
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
public partial class MainWindow : Window
{    
    public MainWindow()
    {
        InitializeComponent();

        // [ Local effect ]
        InitializeTheme();
    }
}
```

> **Switch theme**

If you configure an interpolator, the following functions become available.

```csharp
private static void ReverseThemeWithAnimation()
{
    var condition = ThemeManager.Current == typeof(Dark);
    if (condition)
    {
        ThemeManager.Transition<Light>(TransitionEffects.Theme);
    }
    else
    {
        ThemeManager.Transition<Dark>(TransitionEffects.Theme);
    }
}
```

Of course, considering that gradual transitions may cause performance issues in some scenarios, we also provide a way to not load animations, which is always supported.

```csharp
private static void ReverseThemeWithOutAnimation()
{
    var condition = ThemeManager.Current == typeof(Dark);
    if (condition)
    {
        ThemeManager.Jump<Light>();
    }
    else
    {
        ThemeManager.Jump<Dark>();
    }
}
```

> **callback**

```csharp
partial void OnThemeChanged(Type? oldValue, Type? newValue)
{

}
```

> **Custom theme**

```csharp
public class Light : ITheme
{

}
```

> **Custom Value Converter**

```csharp
public class ObjectConverter : IThemeValueConverter
{
    public object? Convert(Type targetType, string propertyName, object?[] parameters)
    {
        // Parameter validation
        if (parameters == null || parameters.Length != 1 || parameters[0] is not string strValue)
            return null;

        try
        {
            // 1. Attempt resource lookup
            var app = Application.Current;
            if (app != null)
            {
                if (app.TryFindResource(strValue, out var resource) && targetType.IsInstanceOfType(resource))
                {
                    return resource;
                }
            }

            // 2. Use Avalonia built-in type conversion system
            if (TypeUtilities.TryConvert(targetType, strValue, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            // 3. Special handling for Brush type
            if (typeof(IBrush).IsAssignableFrom(targetType))
            {
                var brushConverter = new BrushConverter();
                return brushConverter.Convert(targetType, propertyName, parameters);
            }

            // 4. Use .NET type converter as fallback
            TypeConverter converter = TypeDescriptor.GetConverter(targetType);
            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFromString(null, CultureInfo.InvariantCulture, strValue);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
```