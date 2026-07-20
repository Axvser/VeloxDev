# Theme

Customize the look and feel with VeloxDev's theme system.

## Dark / Light Mode

```csharp
using VeloxDev.DynamicTheme;
ThemeManager.Jump<Dark>();
ThemeManager.Jump<Light>();
```

## Define a Custom Theme

```csharp
[ThemeConfig<ObjectConverter, Dark, Light>(
    nameof(Background),
    ["#1e1e1e"], ["#ffffff"])]
public partial class CustomTheme { }
```

The system auto-detects `PlatformColorValues` and pushes colors to all active views.
