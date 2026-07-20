# Fluent 动画

通过 `Transition<T>.Create()` 构建链式属性过渡。

```csharp
using VeloxDev.TransitionSystem;

var snapshot = Transition<Border>.Create()
    .Property(x => x.Opacity, 0.0)
    .Property(x => x.Width, 200.0)
    .Property(x => x.Height, 300.0)
    .Effect(e =>
    {
        e.Duration = TimeSpan.FromMilliseconds(400);
        e.FPS = 60;
        e.Ease = Eases.Back.Out;
    });

Transition<Border>.Execute(myBorder, snapshot);
```
