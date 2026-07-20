# Animation

Add fluid animations to your workflow UI.

```csharp
// Animate a node's position
var animation = new Vector3Animation
{
    From = new Vector3(0, 0, 0),
    To = new Vector3(100, 200, 0),
    Duration = TimeSpan.FromMilliseconds(300),
    Easing = Easing.CubicOut,
};
animation.Run(node.Anchor);
```

VeloxDev supports:
- **Position / size / opacity transitions**
- **Fluent** animation engine for complex sequences
- **Snapshot**-based state transitions for undo/redo
