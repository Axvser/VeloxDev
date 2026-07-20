# Frame Loop Architecture

The frame loop is the heartbeat of the reactive workflow canvas, driven by **`MonoBehaviourManager`** — a Unity-inspired multi-channel lifecycle system.

---

## Multi-Channel Architecture

```mermaid
flowchart TB
    subgraph Manager[MonoBehaviourManager]
        direction LR
        C1[Channel: default\n60 FPS]
        C2[Channel: physics\n120 FPS]
        C3[Channel: ui\n30 FPS]
    end

    subgraph C1_Behaviours[default channel behaviours]
        B1[BehaviourA]
        B2[BehaviourB]
    end

    subgraph Lifecycle[Per-Behaviour Lifecycle]
        Awake --> Start
        Start --> Update
        Update --> LateUpdate
        Update --> FixedUpdate
    end

    Manager --> C1
    Manager --> C2
    Manager --> C3
    C1 --> C1_Behaviours
    C1_Behaviours --> Lifecycle
```

## Channel State Machine

```mermaid
stateDiagram-v2
    [*] --> Stopped
    Stopped --> Running : Start()
    Running --> Paused : Pause()
    Paused --> Running : Resume()
    Running --> Stopped : StopAsync()
    Paused --> Stopped : StopAsync()
    Stopped --> [*]
```

## Frame Loop Internals

Each channel runs two independent loops:

| Loop | Rate | Drives | Default |
|------|------|--------|---------|
| **Update** | Configurable FPS (1–1000) | `Update()`, `LateUpdate()` | 60 FPS |
| **FixedUpdate** | Fixed interval (ms) | `FixedUpdate()` | 16 ms (~60 Hz) |

Both loops support two execution modes:

- **Thread mode**: Dedicated background thread with precision spin-wait
- **Async mode**: `async/await` loop (set via `SetUseAsyncLoop(true)`)

## Performance Features

- **Coalescing**: Redundant invalidations within the same frame are batched
- **TimeScale**: 0–10× speed multiplier for slow-motion or fast-forward
- **Object pooling**: FrameEventArgs and config change requests are pooled to reduce GC pressure
- **Precision sleep**: Hybrid spin-wait + `Thread.Sleep(1)` for sub-millisecond accuracy
