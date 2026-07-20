# Fluent

You can build animations using the same API in any supported GUI framework. We provide the most elegant approach to date, with a focus on type safety and thread safety.

> **Build**

In the following example, you will create an animation to modify the horizontal offset and fill color of a Rectangle element.

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

You can split the animation process into multiple segments and then connect them in sequence.

```csharp
Transition<Rectangle>.Create()
    .Property()
    .Effect()
    .Then()  // Connect and execute the next animation immediately
    .Property()
    .Effect()
    .AwaitThen() // Connect and wait for a specific time before executing the next animation
    .Property()
    .Effect();
```

> **Control**

Use the following code to control the animation switch.

```csharp
animation.Execute(rect); // Start, the animation acts on the rect instance

Transition.Exit(rect); // Terminate
```

Starting the animation is thread-safe, so there is no need to worry about the following scenarios.

```csharp
_ = Task.Run(() =>
{
    animation.Execute(rect);
});
```

> **Custom animation support**

The IInterpolable interface allows types to directly participate in interpolation. By implementing this interface, when you build animations in Fluent, the Property function can directly use custom types.

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

> **Custom Easing Effect**

The IEaseCalculator interface allows you to customize easing effects.

```csharp
public class EaseInSine : IEaseCalculator
{
    public double Ease(double t) => 1 - Math.Cos(t * Math.PI / 2);
}
```