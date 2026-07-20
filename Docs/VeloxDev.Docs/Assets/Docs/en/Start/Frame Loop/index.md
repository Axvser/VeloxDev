# Frame Loop

Frame loop behavior is common in scenarios such as physics simulation and state monitoring. Now, you can build it with a feature.

> **Build**

You can create multiple groups based on the channel parameter, and you can specify the refresh rate using the fps parameter.

```csharp
[MonoBehaviour(channel:"game", fps:60)]
private partial class Component
{
    public Component() => InitializeMonoBehaviour();


    partial void Awake() { }

    partial void Start() { }

    partial void Update(FrameEventArgs e) { }

    partial void LateUpdate(FrameEventArgs e) { }

    partial void FixedUpdate(FrameEventArgs e) { }
}
```

> **Control**

Basic control can be implemented based on these static functions.

```csharp
MonoBehaviourManager.Start("game"); // Start

MonoBehaviourManager.Pause("game"); // Pause (not equal to stop)

MonoBehaviourManager.Resume("game"); // Resume

await MonoBehaviourManager.StopAsync("game"); // Stop
```

You can modify the time scale and refresh rate at any time.

```csharp
MonoBehaviourManager.SetTimeScale(0.5f, "game"); // time scale

MonoBehaviourManager.SetTargetFPS(30, "game"); // frame rate
```

> **Status Query**

```csharp
bool isRun = MonoBehaviourManager.IsRunning("game")；

bool isPaused = MonoBehaviourManager.IsPaused("game")；
```

> **FrameEventArgs**

| Property | Type | Description |
| --- | --- | --- |
| DeltaTime | TimeSpan | Time since previous frame |
| TotalTime | TimeSpan | Total time |
| TargetFPS | int | Target refresh rate |
| CurrentFPS | int | Real-time refresh rate |