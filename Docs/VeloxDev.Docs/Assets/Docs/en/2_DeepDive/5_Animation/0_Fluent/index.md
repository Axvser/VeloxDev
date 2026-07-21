# Fluent Animation

Build chained property transitions using `Transition<T>.Create()`.

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

The `Property(lambda, value)` method captures property paths; `Effect()` applies timing and easing. Use `.AwaitThen(...)` to insert delays between segments.
