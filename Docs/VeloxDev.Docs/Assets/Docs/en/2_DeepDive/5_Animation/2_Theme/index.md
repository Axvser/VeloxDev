# Theme Animation

Theme transitions trigger automatically when calling `ThemeManager.Transition<T>()`.

```csharp
using VeloxDev.DynamicTheme;

// Smooth transition to Light over 500ms
ThemeManager.Transition<Light>(new TransitionEffect
{
	Duration = TimeSpan.FromMilliseconds(500),
	FPS = 60,
	Ease = Eases.Cubic.Out
});

// Instant switch (no animation)
ThemeManager.Jump<Dark>();
```

`ThemeManager` automatically computes interpolation frames for all `[ThemeConfig]` properties on registered objects. Requires `SetPlatformInterpolator()` to be called during initialization.
