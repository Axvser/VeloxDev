# Frame Loop

Add a Unity-inspired per-frame lifecycle to any class using `[MonoBehaviour]` — works in **console apps** too, no GUI required.

---

## Demo

```
Awake — behaviour initialized
Start — first frame
Frame 30 | Δ=33ms | Total=1.0s    ← logs every ~1s at 30 FPS
Frame 60 | Δ=33ms | Total=2.0s
```

Press Enter to stop, clean exit.

## Steps

### 1. Install

```shell
dotnet add package VeloxDev.Core
```

### 2. Write Code

`Program.cs`:

```csharp
using VeloxDev.MonoBehaviour;
using VeloxDev.TimeLine;

[MonoBehaviour(channel: "demo", fps: 30)]
public partial class MyBehaviour
{
    private int _frameCount;

    // Generator emits partial declarations; user implements partial bodies
    partial void Awake() => Console.WriteLine("Awake — behaviour initialized");
    partial void Start() => Console.WriteLine("Start — first frame");

    partial void Update(FrameEventArgs e)
    {
        _frameCount++;
        if (_frameCount % 30 == 0)
            Console.WriteLine($"Frame {_frameCount} | Δ={e.DeltaTime.TotalMilliseconds:F0}ms | Total={e.TotalTime.TotalSeconds:F1}s");
    }
}

var behaviour = new MyBehaviour();
behaviour.InitializeMonoBehaviour();  // [MonoBehaviour] generator injects: register to channel
MonoBehaviourManager.Start("demo");   // Start the channel

Console.WriteLine("Frame loop running. Press Enter to stop.");
Console.ReadLine();

await MonoBehaviourManager.StopAsync("demo");
Console.WriteLine("Stopped.");
```

### 3. Run

```shell
dotnet run
```

## Lifecycle Methods

| Method | When |
|--------|------|
| `Awake()` | Channel starts (once) |
| `Start()` | Before first Update (once) |
| `Update(FrameEventArgs e)` | Every frame |
| `LateUpdate(FrameEventArgs e)` | After Update |
| `FixedUpdate(FrameEventArgs e)` | Fixed interval (default 16ms) |

## Runtime Control

```csharp
MonoBehaviourManager.Pause("demo");
MonoBehaviourManager.Resume("demo");
MonoBehaviourManager.SetTargetFPS(60, "demo");
MonoBehaviourManager.SetTimeScale(2.0f, "demo");
```

## FrameEventArgs

| Property | Description |
|----------|-------------|
| `DeltaTime` | Time since last frame |
| `TotalTime` | Total channel runtime |
| `CurrentFPS` | Actual FPS |
| `TargetFPS` | Target FPS |
```


