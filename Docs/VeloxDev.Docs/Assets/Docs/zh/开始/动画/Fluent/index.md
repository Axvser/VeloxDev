# Fluent

您可在任何支持的GUI框架中使用相同的API构建动画，我们提供了迄今为止最优雅的做法，并且注重类型安全与线程安全

> **构建**

在以下示例中，您将创建一个动画来修改矩形（Rectangle）元素的水平偏移量和填充颜色

```csharp
private static readonly Transition<Rectangle>.StateSnapshot animation =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, 400)
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(1, 0), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.DeepSkyBlue, 0),
                        new GradientStop(Colors.MediumPurple, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut
            });
```

可以将动画过程拆分为多个片段，然后按顺序衔接起来

```csharp
Transition<Rectangle>.Create()
    .Property()
    .Effect()
    .Then()  // 衔接，立即执行下一个动画
    .Property()
    .Effect()
    .AwaitThen() // 衔接，等待特定时间后才执行下一个动画
    .Property()
    .Effect();
```

> **控制**

使用下述代码控制动画开关

```csharp
animation.Execute(rect); // 启动，动画作用于 rect 实例

Transition.Exit(rect); // 终结
```

动画的启动是线程安全的，完全不必担忧下述场景

```csharp
_ = Task.Run(() =>
{
    animation.Execute(rect);
});
```

> **自定义动画支持**

IInterpolable 接口允许类型直接参与到插值，实现此接口，您在 Fluent 构建动画时，Property 函数可直接使用自定义的类型

```csharp
public sealed class MyVector : IInterpolable
{
    public double X { get; set; }
    public double Y { get; set; }

    public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
    {
        if (steps <= 0) return [];

        var from = start as MyVector ?? new MyVector();
        var to = end as MyVector ?? from;

        if (steps == 1) return [to];

        List<object?> result = new(steps);

        for (int i = 0; i < steps; i++)
        {
            var t = (double)i / (steps - 1);
            result.Add(new MyVector
            {
                X = from.X + (to.X - from.X) * t,
                Y = from.Y + (to.Y - from.Y) * t
            });
        }

        result[0] = start;
        result[^1] = end;
        return result;
    }
}
```

> **自定义缓动效果**

IEaseCalculator 接口允许您定制缓动效果

```csharp
public class EaseInSine : IEaseCalculator
{
    public double Ease(double t) => 1 - Math.Cos(t * Math.PI / 2);
}
```