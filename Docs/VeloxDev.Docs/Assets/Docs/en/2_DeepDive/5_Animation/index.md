# Animation Architecture

The animation system provides declarative, composable animations.

## Three Engines

| Engine | Use Case | Mechanism |
|--------|----------|-----------|
| **Fluent** | Complex multi-property sequences | Easing curves + key frames |
| **Snapshot** | State transitions (open/close, add/remove) | Captures before/after state, interpolates |
| **Theme** | Dark/Light mode transitions | Cross-fades between theme resource values |
