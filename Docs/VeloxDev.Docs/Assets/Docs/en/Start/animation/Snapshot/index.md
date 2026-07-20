# Snapshot

Animation is based on interpolation between two states, and the snapshot mechanism is one of its implementations. VeloxDev has exposed it, so you can also use this mechanism to create animations, or for state saving and restoration (only used in animation scenarios).

> **Take snapshot**

```csharp
// Record all animatable properties, internally recursively searches all deep paths
var snapshot0 = Rec0.SnapshotAll();

// Whitelist mode
var snapshot1 = Rec0.Snapshot(x => ((TranslateTransform)x.RenderTransform!).X, x => x.Fill);

// Blacklist mode
var snapshot2 = Rec0.SnapshotExcept(x => x.Width, x => x.Height, x => x.Fill, x => x.Opacity);
```

> **Use Snapshot**

```csharp
// Essentially still an animation with configurable effects.
snapshot.Effect(TransitionEffects.Empty).Execute(view);
```