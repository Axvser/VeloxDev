# Snapshot Transition

Capture current state via `SnapshotAll()`, mutate properties, then replay the transition.

```csharp
using VeloxDev.TransitionSystem;

// 1. Capture current state
var snapshot = myElement.SnapshotAll();

// 2. Mutate properties directly
myElement.Opacity = 0;
myElement.RenderTransform = new TranslateTransform(100, 0);

// 3. Set effect and replay
snapshot.Effect(new TransitionEffect
{
	Duration = TimeSpan.FromMilliseconds(300),
	FPS = 60,
	Ease = Eases.Cubic.Out
});

Transition<FrameworkElement>.Execute(myElement, snapshot);
```

Use `Snapshot(x => x.Opacity, x => x.Width)` for selective capture, or `SnapshotAll()` for all animatable properties.
