# Frame Loop

The frame loop drives reactive updates to the workflow canvas.

```csharp
// Configure frame rate
var loop = new FrameLoop(TimeSpan.FromMilliseconds(16)); // ~60 FPS

loop.Tick += (_, dt) =>
{
    // Update animations, reactive layouts, etc.
    InvalidateVisual();
};

loop.Start();
```

The frame loop batches invalidations and coalesces redundant updates, ensuring smooth 60 FPS rendering even with hundreds of nodes.
