# Theme

Add dynamic Dark/Light switching with animated transitions. The theme system requires a **GUI project** (WPF/Avalonia/WinUI).

---

## Step 1 — Create a WPF Project

```shell
dotnet new wpf -n MyThemedApp
cd MyThemedApp
dotnet add package VeloxDev.WPF
```

## Step 2 — Decorate Your Window with ThemeConfig

Paste into `MainWindow.xaml.cs` (replacing the existing partial class):

```csharp
using System.Windows;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

// Stack [ThemeConfig] attributes to map properties across themes
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InitializeTheme(); // Must be called AFTER InitializeComponent()

        // Required for animated transitions
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        ThemeManager.StartModel = StartModel.Cache;
    }

    // Switch themes with animation
    private void ReverseThemeWithAnimation()
    {
        var target = ThemeManager.Current == typeof(Dark) ? typeof(Light) : typeof(Dark);
        ThemeManager.Transition(target, TransitionEffects.Theme);
    }

    // Switch themes instantly
    private void ReverseThemeInstant()
    {
        if (ThemeManager.Current == typeof(Dark))
            ThemeManager.Jump<Light>();
        else
            ThemeManager.Jump<Dark>();
    }

    // Lifecycle hook — called automatically on every theme change
    partial void OnThemeChanged(Type? oldTheme, Type? newTheme)
    {
        MessageBox.Show($"Theme: {oldTheme?.Name} → {newTheme?.Name}");
    }
}
```

## Step 3 — Add Toggle Button in XAML

In `MainWindow.xaml`:

```xml
<Window x:Class="Demo.MainWindow" ...>
    <StackPanel>
        <TextBlock Text="Hello VeloxDev!" FontSize="24" />
        <Button Click="ReverseThemeWithAnimation" Content="Toggle Theme" />
    </StackPanel>
</Window>
```

## Step 4 — Run

```shell
dotnet run
```

Click the button — the window background and text color animate between light and dark.

## Key APIs

| API | Purpose |
|-----|---------|
| `[ThemeConfig<TConverter, T1, T2>(...)]` | Declares property-value mappings per theme |
| `InitializeTheme()` | Generated method called after `InitializeComponent()` |
| `ThemeManager.Jump<T>()` | Instant switch |
| `ThemeManager.Transition<T>(effect)` | Animated switch |
| `ThemeManager.SetPlatformInterpolator()` | Required for transitions |
| `partial void OnThemeChanged(...)` | Lifecycle hook |
