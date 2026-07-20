# 主题动画

主题切换时自动触发过渡动画。

```csharp
using VeloxDev.DynamicTheme;

// 500ms 内从当前主题平滑过渡到 Light
ThemeManager.Transition<Light>(new TransitionEffect
{
    Duration = TimeSpan.FromMilliseconds(500),
    FPS = 60,
    Ease = Eases.Cubic.Out
});

// 即时切换（无动画）
ThemeManager.Jump<Dark>();
```

ThemeManager 自动计算每个注册对象的所有 `[ThemeConfig]` 属性的插值帧。
