# Frame Loop Architecture

The frame loop is the heartbeat of the reactive workflow canvas.

## Design

```
FrameLoop (16ms interval)
  ├── Collect invalidated regions
  ├── Update active animations (interpolation)
  ├── Re-layout dirty nodes
  ├── Render frame
  └── Report elapsed time (dt) to subscribers
```

## Key Features

- **Coalescing**: redundant invalidations within the same frame are batched
- **Throttling**: a minimum interval prevents starvation
- **Pause/Resume**: `loop.Stop()` / `loop.Start()` for power management
